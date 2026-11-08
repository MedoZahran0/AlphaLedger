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
    private const string ReleaseApiUrl = "https://api.github.com/repos/MedoZahran0/AlphaLedger/releases/latest";
    private const string UpdateFileName = "AlphaLedger-Update.exe";
    private const string PatcherFileName = "AlphaLedger-Patcher.ps1";
    private const long MaximumInstallerSize = 250 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public GitHubUpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AlphaLedger", GetCurrentVersion().ToString()));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<ReleaseUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ReleaseApiUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub returned an empty release response.");

        if (release.Draft || release.Prerelease ||
            !TryParseVersion(release.TagName, out var releaseVersion) ||
            releaseVersion <= GetCurrentVersion())
            return null;

        var fullAsset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, UpdateFileName, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"The latest release does not contain {UpdateFileName}.");
        var fullUris = GetAssetDownloadUris(fullAsset);
        var fullDigest = RequireSha256(fullAsset);

        var deltaName = $"AlphaLedger-Update-from-v{GetCurrentVersion().ToString(3)}.delta";
        var deltaAsset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, deltaName, StringComparison.Ordinal));
        var patcherAsset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, PatcherFileName, StringComparison.Ordinal));

        if (deltaAsset is null || patcherAsset is null)
            return new ReleaseUpdate(release.TagName, releaseVersion, fullUris, fullDigest, null, null, null, null);

        return new ReleaseUpdate(
            release.TagName,
            releaseVersion,
            fullUris,
            fullDigest,
            GetAssetDownloadUris(deltaAsset),
            RequireSha256(deltaAsset),
            GetAssetDownloadUris(patcherAsset),
            RequireSha256(patcherAsset));
    }

    public async Task<DownloadedUpdate> DownloadUpdateAsync(
        ReleaseUpdate update,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AlphaLedger", "Updates");
        Directory.CreateDirectory(directory);

        if (update.DeltaDownloadUris is not null &&
            update.DeltaDigest is not null &&
            update.PatcherDownloadUris is not null &&
            update.PatcherDigest is not null)
        {
            var deltaPath = Path.Combine(directory, $"AlphaLedger-Update-{update.Version}.delta");
            try
            {
                var verifiedDeltaPath = await DownloadAndVerifyAssetAsync(
                    update.DeltaDownloadUris,
                    update.DeltaDigest,
                    deltaPath,
                    progress,
                    cancellationToken);
                var patcherPath = await DownloadAndVerifyAssetAsync(
                    update.PatcherDownloadUris,
                    update.PatcherDigest,
                    Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(PatcherFileName)}-{update.Version}.ps1"),
                    progress,
                    cancellationToken);
                return new DownloadedUpdate(verifiedDeltaPath, patcherPath, true);
            }
            catch (Exception deltaError) when (deltaError is not OperationCanceledException)
            {
                File.Delete(deltaPath);
                try
                {
                    var fallbackFullPath = await DownloadAndVerifyAssetAsync(
                        update.DownloadUris,
                        update.Digest,
                        Path.Combine(directory, $"{UpdateFileName[..^4]}-{update.Version}.exe"),
                        progress,
                        cancellationToken);
                    return new DownloadedUpdate(fallbackFullPath, null, false);
                }
                catch (Exception fullUpdateError) when (fullUpdateError is not OperationCanceledException)
                {
                    throw new AggregateException(
                        "The differential update failed, and the full update fallback could not be downloaded.",
                        deltaError,
                        fullUpdateError);
                }
            }
        }

        var fullPath = await DownloadAndVerifyAssetAsync(
            update.DownloadUris,
            update.Digest,
            Path.Combine(directory, $"{UpdateFileName[..^4]}-{update.Version}.exe"),
            progress,
            cancellationToken);
        return new DownloadedUpdate(fullPath, null, false);
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

    private static IReadOnlyList<Uri> GetAssetDownloadUris(GitHubReleaseAsset asset)
    {
        if (asset.Id <= 0 ||
            !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var browserUri) ||
            browserUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"The release asset {asset.Name} does not have valid download information.");

        var apiUri = new Uri(
            $"https://api.github.com/repos/MedoZahran0/AlphaLedger/releases/assets/{asset.Id}",
            UriKind.Absolute);
        return [apiUri, browserUri];
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
           (int)statusCode >= 500;

    private static bool IsTransientDownloadFailure(Exception exception)
        => exception is HttpRequestException or IOException or TaskCanceledException;

    private static string RequireSha256(GitHubReleaseAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.Digest) ||
            !asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The release asset {asset.Name} is missing its SHA-256 digest.");
        return asset.Digest;
    }

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
        IReadOnlyList<Uri> DownloadUris,
        string Digest,
        IReadOnlyList<Uri>? DeltaDownloadUris,
        string? DeltaDigest,
        IReadOnlyList<Uri>? PatcherDownloadUris,
        string? PatcherDigest);
    public sealed record DownloadedUpdate(string UpdatePath, string? PatcherPath, bool IsDelta);
    public sealed record DownloadProgress(long BytesReceived, long? TotalBytes);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
