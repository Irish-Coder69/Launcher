using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace Launcher.Core.Services;

public sealed record LauncherUpdatePackage(Version Version, string? DownloadUrl, string? Checksum);

public sealed record LauncherUpdateCheckResult(bool Success, string? ErrorMessage, Version? CurrentVersion, LauncherUpdatePackage? LatestPackage)
{
    public bool IsUpdateAvailable => Success && CurrentVersion is not null && LatestPackage is not null && LatestPackage.Version > CurrentVersion;
}

public sealed record LauncherUpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? PercentComplete => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value * 100d : null;
}

/// <summary>
/// Checks the update feed for a newer Launcher release and downloads the installer with progress reporting.
/// </summary>
public sealed class LauncherUpdateService
{
    private static readonly HttpClient HttpClient = new();

    public async Task<LauncherUpdateCheckResult> CheckForUpdateAsync(string updateUrl, string currentVersionText, CancellationToken cancellationToken = default)
    {
        if (!TryParseVersion(currentVersionText, out var currentVersion))
        {
            return new LauncherUpdateCheckResult(false, "Current version could not be parsed from version.txt.\nDetected: " + currentVersionText, null, null);
        }

        try
        {
            var resolvedUrl = ResolveUpdateUrl(updateUrl);
            var payload = await GetUpdatePayloadAsync(resolvedUrl, cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(payload);
            if (!TryGetLatestUpdatePackage(document.RootElement, out var latestPackage) || latestPackage is null)
            {
                return new LauncherUpdateCheckResult(false, "Update feed did not return a version entry.", currentVersion, null);
            }

            return new LauncherUpdateCheckResult(true, null, currentVersion, latestPackage);
        }
        catch (Exception ex)
        {
            return new LauncherUpdateCheckResult(false, ex.Message, currentVersion, null);
        }
    }

    public async Task DownloadFileAsync(string downloadUrl, string destinationPath, IProgress<LauncherUpdateDownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.UserAgent.ParseAdd("Launcher-Native");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long bytesReceived = 0;
        int bytesRead;

        progress?.Report(new LauncherUpdateDownloadProgress(0, totalBytes));

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            bytesReceived += bytesRead;
            progress?.Report(new LauncherUpdateDownloadProgress(bytesReceived, totalBytes));
        }
    }

    public static bool VerifyChecksum(string filePath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return true;
        }

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));

        return string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> GetUpdatePayloadAsync(string updateUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, updateUrl);
        request.Headers.UserAgent.ParseAdd("Launcher-Native");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveUpdateUrl(string updateUrl)
    {
        return TryResolveGitHubVersionsFeed(updateUrl, out var resolvedUrl) ? resolvedUrl : updateUrl;
    }

    private static bool TryResolveGitHubVersionsFeed(string updateUrl, out string resolvedUrl)
    {
        resolvedUrl = updateUrl;

        if (string.IsNullOrWhiteSpace(updateUrl) || !Uri.TryCreate(updateUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 ||
            !string.Equals(segments[3], "update", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[^1], "versions.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        resolvedUrl = $"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest";
        return true;
    }

    private static bool TryGetLatestUpdatePackage(JsonElement rootElement, out LauncherUpdatePackage? package)
    {
        package = null;

        if (rootElement.ValueKind == JsonValueKind.Array)
        {
            LauncherUpdatePackage? arrayLatestPackage = null;

            foreach (var item in rootElement.EnumerateArray())
            {
                if (!TryGetVersionText(item, out var versionText) || !TryParseVersion(versionText, out var parsedVersion))
                {
                    continue;
                }

                if (arrayLatestPackage is null || parsedVersion > arrayLatestPackage.Version)
                {
                    package = new LauncherUpdatePackage(
                        parsedVersion,
                        TryGetStringProperty(item, "downloadUrl"),
                        TryNormalizeSha256(TryGetStringProperty(item, "checksum")));
                    arrayLatestPackage = package;
                }
            }

            if (arrayLatestPackage is null)
            {
                return false;
            }

            package = arrayLatestPackage;
            return true;
        }

        if (rootElement.ValueKind == JsonValueKind.Object && TryGetVersionText(rootElement, out var objectVersionText))
        {
            if (!TryParseVersion(objectVersionText, out var parsedVersion))
            {
                return false;
            }

            var downloadUrl = TryGetStringProperty(rootElement, "downloadUrl");
            var checksum = TryNormalizeSha256(TryGetStringProperty(rootElement, "checksum"));

            if (rootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var candidateUrl = TryGetStringProperty(asset, "browser_download_url");
                    if (string.IsNullOrWhiteSpace(candidateUrl) || !candidateUrl.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    downloadUrl = candidateUrl;
                    var digest = TryGetStringProperty(asset, "digest");
                    if (!string.IsNullOrWhiteSpace(digest))
                    {
                        checksum = TryNormalizeSha256(digest);
                    }

                    break;
                }
            }

            package = new LauncherUpdatePackage(parsedVersion, downloadUrl, checksum);
            return true;
        }

        return false;
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? TryNormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? trimmed["sha256:".Length..]
            : trimmed;
    }

    private static bool TryGetVersionText(JsonElement element, out string? versionText)
    {
        versionText = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var propertyName in new[] { "version", "tag_name", "tagName" })
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            var candidate = property.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                versionText = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseVersion(string? versionText, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        var normalized = versionText.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (!Version.TryParse(normalized, out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }
}
