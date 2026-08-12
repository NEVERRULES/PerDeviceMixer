namespace PerDeviceMixer.Core;

public static class PerDeviceMixerDataPaths
{
    public const string OverrideEnvironmentVariable = "PERDEVICEMIXER_DATA_DIRECTORY";

    public static string GetDataDirectory()
    {
        var overrideDirectory = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PerDeviceMixer");
    }
}
