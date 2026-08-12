using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

internal sealed record ReleaseAssetInfo(
    string Name,
    Uri DownloadUrl,
    long Size,
    string? Digest);

internal sealed record UpdateReleaseInfo(
    ReleaseVersion Version,
    string Tag,
    string Name,
    Uri ReleaseUrl,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    IReadOnlyList<ReleaseAssetInfo> Assets);

internal sealed class GitHubUpdateService : IDisposable
{
    private static readonly Uri ReleasesApi = new(
        "https://api.github.com/repos/NEVERRULES/PerDeviceMixer/releases?per_page=30");

    private readonly HttpClient _httpClient;
    private readonly string _updatesDirectory;
    private bool _disposed;

    public GitHubUpdateService(
        HttpMessageHandler? handler = null,
        string? updatesDirectory = null,
        TimeSpan? httpTimeout = null)
    {
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.Timeout = httpTimeout ?? TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "PerDeviceMixer",
            ApplicationVersionInfo.Current.Replace('+', '-')));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _updatesDirectory = updatesDirectory ?? Path.Combine(
            PerDeviceMixerDataPaths.GetDataDirectory(),
            "updates");
    }

    public async Task<UpdateReleaseInfo?> FindAvailableReleaseAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReleaseVersion.TryParse(currentVersion, out var current) || current is null)
        {
            throw new InvalidOperationException("当前应用版本号无法用于更新比较。");
        }

        using var response = await _httpClient.GetAsync(ReleasesApi, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(
                           stream,
                           cancellationToken: cancellationToken).ConfigureAwait(false)
                       ?? [];

        return releases
            .Where(release => !release.Draft)
            .Where(release => current.IsPrerelease || !release.Prerelease)
            .Select(TryCreateRelease)
            .Where(release => release is not null && release.Version.CompareTo(current) > 0)
            .Cast<UpdateReleaseInfo>()
            .OrderByDescending(release => release.Version)
            .FirstOrDefault();
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateReleaseInfo release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var installer = release.Assets.SingleOrDefault(asset =>
            asset.Name.StartsWith("PerDeviceMixer-", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith("-win-x64-Setup.exe", StringComparison.OrdinalIgnoreCase));
        var checksums = release.Assets.SingleOrDefault(asset =>
            string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (installer is null || checksums is null)
        {
            throw new InvalidOperationException("该版本缺少安装包或 SHA256SUMS.txt。");
        }

        ValidateReleaseAsset(installer, maximumBytes: 250L * 1024 * 1024);
        ValidateReleaseAsset(checksums, maximumBytes: 64 * 1024);
        var expectedHash = await DownloadExpectedHashAsync(
            checksums,
            installer.Name,
            cancellationToken).ConfigureAwait(false);

        var versionDirectory = Path.Combine(_updatesDirectory, SanitizePathSegment(release.Tag));
        Directory.CreateDirectory(versionDirectory);
        var finalPath = Path.Combine(versionDirectory, installer.Name);
        var partialPath = finalPath + ".partial";
        try
        {
            if (File.Exists(finalPath))
            {
                var existingHash = await ComputeSha256Async(finalPath, cancellationToken).ConfigureAwait(false);
                if (string.Equals(existingHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(100);
                    return finalPath;
                }

                File.Delete(finalPath);
            }

            if (File.Exists(partialPath)) File.Delete(partialPath);
            using var response = await _httpClient.GetAsync(
                installer.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != installer.Size)
            {
                throw new InvalidDataException("安装包下载大小与 GitHub Release 记录不一致。");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                             partialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    total += read;
                    progress?.Report((int)Math.Clamp(total * 100L / installer.Size, 0, 99));
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (total != installer.Size)
                {
                    throw new InvalidDataException("安装包下载不完整。");
                }
            }

            var actualHash = await ComputeSha256Async(partialPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase) ||
                !DigestMatches(installer.Digest, actualHash))
            {
                throw new InvalidDataException("安装包 SHA-256 校验失败，已取消安装。");
            }

            File.Move(partialPath, finalPath, overwrite: true);
            progress?.Report(100);
            return finalPath;
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    public void CleanupOldUpdates(DateTimeOffset now)
    {
        if (!Directory.Exists(_updatesDirectory)) return;
        var root = Path.GetFullPath(_updatesDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var directory in Directory.EnumerateDirectories(_updatesDirectory))
        {
            try
            {
                var resolved = Path.GetFullPath(directory);
                var attributes = File.GetAttributes(resolved);
                if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    !attributes.HasFlag(FileAttributes.ReparsePoint) &&
                    Directory.GetLastWriteTimeUtc(resolved) < now.UtcDateTime.AddDays(-7))
                {
                    Directory.Delete(resolved, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };
        foreach (var partial in Directory.EnumerateFiles(_updatesDirectory, "*.partial", enumeration))
        {
            try
            {
                var resolved = Path.GetFullPath(partial);
                if (resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase)) File.Delete(resolved);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task<string> DownloadExpectedHashAsync(
        ReleaseAssetInfo checksums,
        string installerName,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(checksums.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 65536)
        {
            throw new InvalidDataException("SHA256SUMS.txt 超出允许大小。");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                string.Equals(parts[1].TrimStart('*'), installerName, StringComparison.OrdinalIgnoreCase) &&
                parts[0].Length == 64 &&
                parts[0].All(Uri.IsHexDigit))
            {
                return parts[0].ToLowerInvariant();
            }
        }

        throw new InvalidDataException("SHA256SUMS.txt 中没有对应安装包的校验值。");
    }

    private static UpdateReleaseInfo? TryCreateRelease(GitHubReleaseDto release)
    {
        if (!ReleaseVersion.TryParse(release.TagName, out var version) || version is null ||
            !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releaseUrl) ||
            !IsTrustedGitHubUrl(releaseUrl))
        {
            return null;
        }

        var assets = new List<ReleaseAssetInfo>();
        foreach (var asset in release.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Name) || asset.Size <= 0 ||
                !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var assetUrl) ||
                !IsTrustedGitHubUrl(assetUrl))
            {
                continue;
            }

            assets.Add(new ReleaseAssetInfo(asset.Name, assetUrl, asset.Size, asset.Digest));
        }

        return new UpdateReleaseInfo(
            version,
            release.TagName,
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            releaseUrl,
            release.PublishedAt,
            release.Prerelease,
            assets);
    }

    private static void ValidateReleaseAsset(ReleaseAssetInfo asset, long maximumBytes)
    {
        if (!IsTrustedGitHubUrl(asset.DownloadUrl) || asset.Size <= 0 || asset.Size > maximumBytes)
        {
            throw new InvalidDataException($"Release 资源 {asset.Name} 的地址或大小无效。");
        }
    }

    private static bool IsTrustedGitHubUrl(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);

    private static bool DigestMatches(string? digest, string actualHash)
    {
        if (string.IsNullOrWhiteSpace(digest)) return true;
        const string prefix = "sha256:";
        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(digest[prefix.Length..], actualHash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static string SanitizePathSegment(string value)
    {
        var sanitized = new string(value
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto> Assets { get; set; } = [];
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}
