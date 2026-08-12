using System.Diagnostics;

namespace PerDeviceMixer.App;

internal static class SystemSoundSettings
{
    public static void OpenDeviceProperties(string endpointId) =>
        OpenUri($"ms-settings:sound-properties?endpointId={Uri.EscapeDataString(endpointId)}");

    public static void OpenSoundDevices() => OpenUri("ms-settings:sound-devices");

    public static void OpenBluetoothDevices() => OpenUri("ms-settings:bluetooth");

    public static void OpenMonoAudioSettings() => OpenUri("ms-settings:easeofaccess-audio");

    private static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri)
    {
        UseShellExecute = true
    });
}
