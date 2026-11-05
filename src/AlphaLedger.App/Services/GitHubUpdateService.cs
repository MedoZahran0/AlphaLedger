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
    private const string InstallerFileName = "AlphaLedger-Setup.exe";
    private const long MaximumInstallerSize = 250 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public GitHubUpdateService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
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

        var asset = release.Assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, InstallerFileName, StringComparison.Ordinal));
        if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"The latest release does not contain a valid {InstallerFileName} asset.");

        var digest = asset.Digest;
        if (string.IsNullOrWhiteSpace(digest) ||
            !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer release asset is missing its SHA-256 digest.");

        return new ReleaseUpdate(release.TagName, releaseVersion, downloadUri, digest);
    }

    public async Task<string> DownloadInstallerAsync(ReleaseUpdate update, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AlphaLedger", "Updates");
        Directory.CreateDirectory(directory);
        var installerPath = Path.Combine(directory, $"{InstallerFileName[..^4]}-{update.Version}.exe");

        try
        {
            using var response = await _httpClient.GetAsync(
                update.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaximumInstallerSize)
                throw new InvalidDataException("The downloaded installer exceeds the allowed size.");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long totalBytes = 0;
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;
                    if (totalBytes > MaximumInstallerSize)
                        throw new InvalidDataException("The downloaded installer exceeds the allowed size.");
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
            }

            await using var installer = File.OpenRead(installerPath);
            var hash = await SHA256.HashDataAsync(installer, cancellationToken);
            var actualDigest = $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
            if (!string.Equals(actualDigest, update.Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded installer failed its SHA-256 integrity check.");

            return installerPath;
        }
        catch
        {
            File.Delete(installerPath);
            throw;
        }
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

    public sealed record ReleaseUpdate(string TagName, Version Version, Uri DownloadUri, string Digest);

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
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
