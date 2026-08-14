using Microsoft.Win32;
using System.IO;

namespace PerDeviceMixer.App;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PerDeviceMixer";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return IsCurrentExecutableCommand(key?.GetValue(ValueName) as string, Environment.ProcessPath);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定程序路径，不能设置开机自启动。");
        }

        key.SetValue(ValueName, $"\"{executablePath}\" --minimized", RegistryValueKind.String);
    }

    internal static bool IsCurrentExecutableCommand(string? command, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(executablePath)) return false;
        var trimmed = command.Trim();
        var commandPath = trimmed.StartsWith('"')
            ? ExtractQuotedExecutablePath(trimmed)
            : trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (string.IsNullOrWhiteSpace(commandPath)) return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(commandPath),
                Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? ExtractQuotedExecutablePath(string command)
    {
        var closingQuote = command.IndexOf('"', 1);
        return closingQuote > 1 ? command[1..closingQuote] : null;
    }
}
