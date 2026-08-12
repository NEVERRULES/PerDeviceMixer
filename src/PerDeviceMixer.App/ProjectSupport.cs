using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PerDeviceMixer.App;

internal static class ProjectLinks
{
    public const string Project = "https://github.com/NEVERRULES/PerDeviceMixer";
    public const string Releases = Project + "/releases";
    public const string NewIssue = Project + "/issues/new";
}

internal static class ExternalLinkService
{
    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只允许打开项目的 GitHub HTTPS 链接。");
        }

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    public static string CreateIssueUrl(string body)
    {
        var title = Uri.EscapeDataString("[问题反馈] ");
        var encodedBody = Uri.EscapeDataString(body);
        return $"{ProjectLinks.NewIssue}?title={title}&body={encodedBody}";
    }
}

internal static class ApplicationVersionInfo
{
    public static string Current { get; } = GetCurrentVersion();

    private static string GetCurrentVersion()
    {
        var value = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(value)) return "0.0.0";
        var metadata = value.IndexOf('+', StringComparison.Ordinal);
        return metadata >= 0 ? value[..metadata] : value;
    }
}

internal enum ApplicationInstallType
{
    Installed,
    Portable
}

internal static class InstallationDetector
{
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{10F8FE46-850C-4F65-8951-87439A4ED6AB}_is1";

    public static ApplicationInstallType GetInstallType()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: false);
        var installLocation = key?.GetValue("InstallLocation") as string;
        return Detect(installLocation, Environment.ProcessPath);
    }

    internal static ApplicationInstallType Detect(string? installLocation, string? processPath)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || string.IsNullOrWhiteSpace(processPath))
        {
            return ApplicationInstallType.Portable;
        }

        try
        {
            var normalizedLocation = Path.GetFullPath(installLocation)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var normalizedProcessPath = Path.GetFullPath(processPath);
            return normalizedProcessPath.StartsWith(normalizedLocation, StringComparison.OrdinalIgnoreCase)
                ? ApplicationInstallType.Installed
                : ApplicationInstallType.Portable;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ApplicationInstallType.Portable;
        }
    }

    public static string GetDisplayName() => GetInstallType() == ApplicationInstallType.Installed
        ? "安装版"
        : "便携版";
}

internal static class FeedbackInformation
{
    public static string CreateBasic() =>
        $"""
        <!-- 请在下方描述问题发生前后的操作。提交前可删除不希望公开的内容。 -->

        ## 问题描述


        ## 基础信息

        - PerDeviceMixer：{ApplicationVersionInfo.Current}
        - Windows：{Environment.OSVersion.VersionString}
        - 架构：{RuntimeInformation.ProcessArchitecture}
        - 安装类型：{InstallationDetector.GetDisplayName()}
        """;
}
