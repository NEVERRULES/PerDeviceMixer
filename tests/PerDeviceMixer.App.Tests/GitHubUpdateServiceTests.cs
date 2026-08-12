using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using PerDeviceMixer.App;

namespace PerDeviceMixer.App.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task PreviewBuildSelectsTheHighestNewerPreviewOrStableRelease()
    {
        var json = """
            [
              {"tag_name":"v0.3.2","name":"Stable","html_url":"https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/v0.3.2","draft":false,"prerelease":false,"assets":[]},
              {"tag_name":"v0.3.3-preview.1","name":"Preview","html_url":"https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/v0.3.3-preview.1","draft":false,"prerelease":true,"assets":[]},
              {"tag_name":"v9.0.0","name":"Draft","html_url":"https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/v9.0.0","draft":true,"prerelease":false,"assets":[]}
            ]
            """;
        using var service = new GitHubUpdateService(new StaticHandler(_ => JsonResponse(json)));

        var release = await service.FindAvailableReleaseAsync("0.3.2-preview.1");

        Assert.NotNull(release);
        Assert.Equal("0.3.3-preview.1", release.Version.ToString());
    }

    [Fact]
    public async Task StableBuildIgnoresPrereleases()
    {
        var json = """
            [
              {"tag_name":"v1.1.0-preview.1","name":"Preview","html_url":"https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/v1.1.0-preview.1","draft":false,"prerelease":true,"assets":[]},
              {"tag_name":"v1.0.1","name":"Stable","html_url":"https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/v1.0.1","draft":false,"prerelease":false,"assets":[]}
            ]
            """;
        using var service = new GitHubUpdateService(new StaticHandler(_ => JsonResponse(json)));

        var release = await service.FindAvailableReleaseAsync("1.0.0");

        Assert.NotNull(release);
        Assert.Equal("1.0.1", release.Version.ToString());
    }

    [Fact]
    public async Task ReleaseSelectionIgnoresUnparseableTagsAndUntrustedLinks()
    {
        var json = """
            [
              {"tag_name":"not-a-version","name":"Invalid","html_url":"https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/nope","draft":false,"prerelease":false,"assets":[]},
              {"tag_name":"v9.0.0","name":"Untrusted","html_url":"https://example.com/release","draft":false,"prerelease":false,"assets":[]}
            ]
            """;
        using var service = new GitHubUpdateService(new StaticHandler(_ => JsonResponse(json)));

        var release = await service.FindAvailableReleaseAsync("1.0.0");

        Assert.Null(release);
    }

    [Fact]
    public async Task ReleaseCheckHonorsTheConfiguredHttpTimeout()
    {
        using var service = new GitHubUpdateService(
            new AsyncHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new Xunit.Sdk.XunitException("The timeout should cancel the request.");
            }),
            httpTimeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.FindAvailableReleaseAsync("1.0.0"));
    }

    [Fact]
    public async Task ReleaseCheckReportsRateLimiting()
    {
        using var service = new GitHubUpdateService(new StaticHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.FindAvailableReleaseAsync("1.0.0"));
    }

    [Fact]
    public async Task ReleaseCheckReportsAnOfflineNetwork()
    {
        using var service = new GitHubUpdateService(new AsyncHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"))));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.FindAvailableReleaseAsync("1.0.0"));
    }

    [Fact]
    public async Task DownloadInstallerValidatesChecksumAndRemovesPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var installerBytes = Encoding.UTF8.GetBytes("verified installer payload");
        var hash = Convert.ToHexStringLower(SHA256.HashData(installerBytes));
        const string installerName = "PerDeviceMixer-1.0.0-win-x64-Setup.exe";
        var release = CreateRelease(installerName, installerBytes.Length, hash);
        using var service = new GitHubUpdateService(
            new StaticHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? TextResponse($"{hash}  {installerName}\n")
                : BytesResponse(installerBytes)),
            directory.Path);

        var path = await service.DownloadInstallerAsync(release);

        Assert.True(File.Exists(path));
        Assert.Equal(installerBytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadInstallerRejectsChecksumMismatchAndRemovesPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var installerBytes = Encoding.UTF8.GetBytes("tampered installer payload");
        var expectedHash = new string('a', 64);
        const string installerName = "PerDeviceMixer-1.0.0-win-x64-Setup.exe";
        var release = CreateRelease(installerName, installerBytes.Length, expectedHash);
        using var service = new GitHubUpdateService(
            new StaticHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? TextResponse($"{expectedHash}  {installerName}\n")
                : BytesResponse(installerBytes)),
            directory.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadInstallerAsync(release));

        Assert.Empty(Directory.GetFiles(directory.Path, "*.partial", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancelledDownloadRemovesPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var installerBytes = new byte[100_000];
        var hash = Convert.ToHexStringLower(SHA256.HashData(installerBytes));
        const string installerName = "PerDeviceMixer-1.0.0-win-x64-Setup.exe";
        var release = CreateRelease(installerName, installerBytes.Length, hash);
        using var cancellation = new CancellationTokenSource();
        using var service = new GitHubUpdateService(
            new StaticHandler(request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? TextResponse($"{hash}  {installerName}\n")
                : BytesResponse(installerBytes)),
            directory.Path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadInstallerAsync(
            release,
            new InlineProgress(value =>
            {
                if (value > 0) cancellation.Cancel();
            }),
            cancellation.Token));

        Assert.Empty(Directory.GetFiles(directory.Path, "*.partial", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadInstallerRejectsReleaseWithoutRequiredAssets()
    {
        using var directory = new TemporaryDirectory();
        Assert.True(ReleaseVersion.TryParse("1.0.0", out var version));
        var release = new UpdateReleaseInfo(
            version!,
            "v1.0.0",
            "1.0.0",
            new Uri("https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/v1.0.0"),
            DateTimeOffset.UtcNow,
            false,
            []);
        using var service = new GitHubUpdateService(new StaticHandler(_ => throw new Xunit.Sdk.XunitException(
            "HTTP should not be called when assets are missing.")), directory.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadInstallerAsync(release));
    }

    [Fact]
    public void CleanupOldUpdatesRemovesExpiredDirectoriesAndPartialFilesOnly()
    {
        using var directory = new TemporaryDirectory();
        var oldDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "old"));
        var currentDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "current"));
        var partial = Path.Combine(currentDirectory.FullName, "setup.exe.partial");
        File.WriteAllText(partial, "partial");
        var now = DateTimeOffset.UtcNow;
        Directory.SetLastWriteTimeUtc(oldDirectory.FullName, now.UtcDateTime.AddDays(-8));
        Directory.SetLastWriteTimeUtc(currentDirectory.FullName, now.UtcDateTime);
        using var service = new GitHubUpdateService(new StaticHandler(_ => JsonResponse("[]")), directory.Path);

        service.CleanupOldUpdates(now);

        Assert.False(Directory.Exists(oldDirectory.FullName));
        Assert.True(Directory.Exists(currentDirectory.FullName));
        Assert.False(File.Exists(partial));
    }

    private static UpdateReleaseInfo CreateRelease(string installerName, int installerSize, string hash)
    {
        Assert.True(ReleaseVersion.TryParse("1.0.0", out var version));
        return new UpdateReleaseInfo(
            version!,
            "v1.0.0",
            "1.0.0",
            new Uri("https://github.com/NEVERRULES/PerDeviceMixer/releases/tag/v1.0.0"),
            DateTimeOffset.UtcNow,
            false,
            [
                new ReleaseAssetInfo(
                    installerName,
                    new Uri("https://github.com/NEVERRULES/PerDeviceMixer/releases/download/v1.0.0/setup.exe"),
                    installerSize,
                    "sha256:" + hash),
                new ReleaseAssetInfo(
                    "SHA256SUMS.txt",
                    new Uri("https://github.com/NEVERRULES/PerDeviceMixer/releases/download/v1.0.0/SHA256SUMS.txt"),
                    100,
                    null)
            ]);
    }

    private static HttpResponseMessage JsonResponse(string json) => TextResponse(json, "application/json");

    private static HttpResponseMessage TextResponse(string text, string contentType = "text/plain") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, contentType)
    };

    private static HttpResponseMessage BytesResponse(byte[] value) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(value)
    };

    private sealed class StaticHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class AsyncHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PerDeviceMixer.App.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
