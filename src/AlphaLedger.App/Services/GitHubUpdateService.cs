using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlphaLedger.App.Services;

public sealed class GitHubUpdateService : IDisposable
{
    private const string LatestManifestUrl = "https://raw.githubusercontent.com/MedoZahran0/AlphaLedger/main/updates/latest.json";
    private const string UpdateFileName = "AlphaLedger-Update.exe";
    private const long MaximumInstallerSize = 250 * 1024 * 1024;
    private static readonly TimeSpan ReleaseCheckTimeout = TimeSpan.FromSeconds(6);
    private readonly HttpClient _httpClient;

    public GitHubUpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AlphaLedger", GetCurrentVersion().ToString()));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ReleaseUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await GetLatestManifestResponseAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The update manifest is empty.");

        if (!TryParseVersion(manifest.Version, out var releaseVersion))
            throw new InvalidDataException("The update manifest contains an invalid version.");
        if (releaseVersion <= GetCurrentVersion())
            return null;

        const string digestPrefix = "sha256:";
        if (string.IsNullOrWhiteSpace(manifest.Digest) ||
            !manifest.Digest.StartsWith(digestPrefix, StringComparison.OrdinalIgnoreCase) ||
            manifest.Digest.Length != digestPrefix.Length + 64 ||
            manifest.Digest[digestPrefix.Length..].Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The update manifest does not contain a valid SHA-256 digest.");

        var tagName = $"v{releaseVersion}";
        var downloadUri = new Uri(
            $"https://github.com/MedoZahran0/AlphaLedger/releases/download/{tagName}/{UpdateFileName}",
            UriKind.Absolute);
        return new ReleaseUpdate(tagName, releaseVersion, downloadUri, manifest.Digest);
    }

    private async Task<HttpResponseMessage> GetLatestManifestResponseAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReleaseCheckTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestManifestUrl);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    timeout.Token);
                if (!IsTransientStatusCode(response.StatusCode) || attempt == 1)
                    return response;

                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 1 && IsTransientDownloadFailure(ex))
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                lastError = new TimeoutException("Timed out while checking the update manifest.", ex);
                if (attempt < 1)
                    await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
        }

        throw new HttpRequestException(
            $"Could not retrieve the update manifest after two quick attempts. {lastError?.Message}",
            lastError);
    }

    public async Task<DownloadedUpdate> DownloadUpdateAsync(
        ReleaseUpdate update,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AlphaLedger", "Updates");
        Directory.CreateDirectory(directory);
        var fullPath = await DownloadAndVerifyAssetAsync(
            [update.DownloadUri],
            update.Digest,
            Path.Combine(directory, $"{UpdateFileName[..^4]}-{update.Version}.exe"),
            progress,
            cancellationToken);
        return new DownloadedUpdate(fullPath);
    }

    private async Task<string> DownloadAndVerifyAssetAsync(
        IReadOnlyList<Uri> downloadUris,
        string expectedDigest,
        string assetPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var downloadUri in downloadUris)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                    using var response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    if (IsTransientStatusCode(response.StatusCode) && attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(
                            $"GitHub returned HTTP {(int)response.StatusCode} while downloading an update asset.",
                            null,
                            response.StatusCode);
                    }

                    var totalBytesExpected = response.Content.Headers.ContentLength;
                    if (totalBytesExpected is > MaximumInstallerSize)
                        throw new InvalidDataException("The downloaded update exceeds the allowed size.");

                    await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var destination = new FileStream(assetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long totalBytes = 0;
                        int bytesRead;
                        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            totalBytes += bytesRead;
                            if (totalBytes > MaximumInstallerSize)
                                throw new InvalidDataException("The downloaded update exceeds the allowed size.");
                            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            progress?.Report(new DownloadProgress(totalBytes, totalBytesExpected));
                        }
                    }

                    await using var updateStream = File.OpenRead(assetPath);
                    var hash = await SHA256.HashDataAsync(updateStream, cancellationToken);
                    var actualDigest = $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
                    if (!string.Equals(actualDigest, expectedDigest, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded update failed its SHA-256 integrity check.");

                    return assetPath;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    File.Delete(assetPath);
                    throw;
                }
                catch (Exception ex) when (attempt < 2 && IsTransientDownloadFailure(ex))
                {
                    File.Delete(assetPath);
                    lastError = ex;
                    await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
                }
                catch (Exception ex)
                {
                    File.Delete(assetPath);
                    lastError = ex;
                    break;
                }
            }
        }

        throw new HttpRequestException(
            $"All GitHub download routes failed for the update asset. {lastError?.Message}",
            lastError);
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
           (int)statusCode >= 500;

    private static bool IsTransientDownloadFailure(Exception exception)
        => exception is HttpRequestException or IOException or TaskCanceledException;

    public static Version GetCurrentVersion() =>
        typeof(GitHubUpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

    private static bool TryParseVersion(string tagName, out Version version)
    {
        var normalizedTag = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? tagName[1..]
            : tagName;
        if (Version.TryParse(normalizedTag, out var parsedVersion) && parsedVersion is not null)
        {
            version = parsedVersion;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }

    public void Dispose() => _httpClient.Dispose();

    public sealed record ReleaseUpdate(
        string TagName,
        Version Version,
        Uri DownloadUri,
        string Digest);
    public sealed record DownloadedUpdate(string UpdatePath);
    public sealed record DownloadProgress(long BytesReceived, long? TotalBytes);

    private sealed class UpdateManifest
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string? Digest { get; init; }
    }
}
