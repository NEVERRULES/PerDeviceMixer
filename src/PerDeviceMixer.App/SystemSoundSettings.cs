using System.Diagnostics;

namespace PerDeviceMixer.App;

internal static class SystemSoundSettings
{
    public static void OpenDeviceProperties(string endpointId) =>
        OpenUri($"ms-settings:sound-properties?endpointId={Uri.EscapeDataString(endpointId)}");

    public static void OpenSoundDevices() => OpenUri("ms-settings:sound-devices");

    public static void OpenBluetoothDevices() => OpenUri("ms-settings:bluetooth");

    public static void OpenBluetoothDeviceServices()
    {
        // The per-device "Handsfree Telephony" checkbox only exists in the classic
        // Devices and Printers shell folder: right-click the device -> Properties -> Services.
        Process.Start(new ProcessStartInfo(
            "explorer.exe",
            @"shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}")
        {
            UseShellExecute = true
        });
    }

    public static void OpenMonoAudioSettings() => OpenUri("ms-settings:easeofaccess-audio");

    private static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri)
    {
        UseShellExecute = true
    });
}
