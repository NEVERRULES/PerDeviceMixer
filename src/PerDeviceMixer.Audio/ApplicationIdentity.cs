using System.Diagnostics;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.Audio;

internal static class ApplicationIdentity
{
    public static AudioSessionInfo Create(NAudio.CoreAudioApi.AudioSessionControl session)
    {
        var isSystemSounds = Safe(() => session.IsSystemSoundsSession, false);
        var processId = isSystemSounds ? null : Safe<int?>(() => checked((int)session.GetProcessID), null);
        string? executablePath = null;
        string? processName = null;

        if (processId is > 0)
        {
            try
            {
                using var process = Process.GetProcessById(processId.Value);
                processName = process.ProcessName;
                executablePath = process.MainModule?.FileName;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        var displayName = isSystemSounds
            ? "系统声音"
            : FirstNotBlank(
                processName,
                Safe(() => session.DisplayName, string.Empty),
                executablePath is null ? null : Path.GetFileNameWithoutExtension(executablePath),
                "未知应用");

        var applicationKey = isSystemSounds
            ? "system:sounds"
            : executablePath is not null
                ? "path:" + executablePath.ToLowerInvariant()
                : processName is not null
                    ? "exe:" + processName.ToLowerInvariant() + ".exe"
                    : "session:" + Safe(() => session.GetSessionIdentifier, displayName).ToLowerInvariant();

        return new AudioSessionInfo(
            Safe(() => session.GetSessionInstanceIdentifier, Guid.NewGuid().ToString("N")),
            applicationKey,
            displayName,
            executablePath,
            processId,
            isSystemSounds,
            Math.Clamp(Safe(() => session.SimpleAudioVolume.Volume, 1f), 0f, 1f),
            Safe(() => session.SimpleAudioVolume.Mute, false));
    }

    private static string FirstNotBlank(params string?[] candidates) =>
        candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!;

    private static T Safe<T>(Func<T> action, T fallback)
    {
        try
        {
            return action();
        }
        catch
        {
            return fallback;
        }
    }
}
