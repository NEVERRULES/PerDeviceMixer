using System.Runtime.InteropServices;
using PerDeviceMixer.App;

namespace PerDeviceMixer.App.Tests;

public sealed class ProjectSupportTests
{
    [Fact]
    public void IssueUrlRoundTripsUnicodeFeedbackBody()
    {
        const string body = "音量切换后没有恢复 & 设备名称=耳机";

        var url = ExternalLinkService.CreateIssueUrl(body);
        var query = new Uri(url).Query;

        Assert.Contains(Uri.EscapeDataString(body), query, StringComparison.Ordinal);
        Assert.StartsWith(ProjectLinks.NewIssue, url, StringComparison.Ordinal);
    }

    [Fact]
    public void BasicFeedbackContainsOnlyTheDocumentedEnvironmentSummary()
    {
        var body = FeedbackInformation.CreateBasic();

        Assert.Contains(ApplicationVersionInfo.Current, body, StringComparison.Ordinal);
        Assert.Contains(RuntimeInformation.ProcessArchitecture.ToString(), body, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("设备 ID", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profiles.json", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("应用列表", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("日志", body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\AppData\Local\Programs\PerDeviceMixer", @"C:\Users\Test\AppData\Local\Programs\PerDeviceMixer\PerDeviceMixer.App.exe", 0)]
    [InlineData(@"C:\Program Files\PerDeviceMixer", @"D:\Portable\PerDeviceMixer.App.exe", 1)]
    [InlineData(null, @"D:\Portable\PerDeviceMixer.App.exe", 1)]
    public void InstallTypeRequiresTheExecutableToBeInsideTheRegisteredLocation(
        string? installLocation,
        string processPath,
        int expected)
    {
        Assert.Equal((ApplicationInstallType)expected, InstallationDetector.Detect(installLocation, processPath));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\PerDeviceMixer\\PerDeviceMixer.App.exe\" --minimized", "C:\\Program Files\\PerDeviceMixer\\PerDeviceMixer.App.exe", true)]
    [InlineData("\"D:\\Old\\PerDeviceMixer.App.exe\" --minimized", "C:\\Program Files\\PerDeviceMixer\\PerDeviceMixer.App.exe", false)]
    [InlineData("\"C:\\Program Files\\PerDeviceMixer\\PerDeviceMixer.App.exe", "C:\\Program Files\\PerDeviceMixer\\PerDeviceMixer.App.exe", false)]
    public void StartupEntryMustPointToTheCurrentExecutable(
        string command,
        string executablePath,
        bool expected)
    {
        Assert.Equal(expected, StartupManager.IsCurrentExecutableCommand(command, executablePath));
    }
}
