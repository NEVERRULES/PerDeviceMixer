using System.Diagnostics;

namespace PerDeviceMixer.App;

internal static class InstallerHandoff
{
    public static void Launch(
        string installerPath,
        Func<bool> releaseInstanceMutex,
        Func<bool> reacquireInstanceMutex,
        Func<string, bool>? launchInstaller = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        ArgumentNullException.ThrowIfNull(releaseInstanceMutex);
        ArgumentNullException.ThrowIfNull(reacquireInstanceMutex);

        var releasedMutex = releaseInstanceMutex();
        try
        {
            var launched = (launchInstaller ?? StartInstaller)(installerPath);
            if (!launched) throw new InvalidOperationException("安装程序未能启动。");
        }
        catch
        {
            if (releasedMutex) _ = reacquireInstanceMutex();
            throw;
        }
    }

    private static bool StartInstaller(string installerPath)
    {
        using var process = Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Arguments = "/CLOSEAPPLICATIONS"
        });
        return process is not null;
    }
}
