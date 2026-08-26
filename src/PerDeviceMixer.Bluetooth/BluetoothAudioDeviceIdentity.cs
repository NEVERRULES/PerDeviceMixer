using System.Text.RegularExpressions;

namespace PerDeviceMixer.Bluetooth;

public sealed record BluetoothAudioDeviceIdentity(
    ushort VendorId,
    ushort ProductId)
{
    private static readonly Regex HardwareIdPattern = new(
        @"VID&(?:0001)?(?<vendor>[0-9A-F]{4})_PID&(?<product>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public bool IsBeatsFitPro =>
        VendorId == AppleHeadphoneIds.CompanyId &&
        ProductId == AppleHeadphoneIds.BeatsFitProProductId;

    public static bool TryParse(
        string? hardwareInstanceId,
        out BluetoothAudioDeviceIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(hardwareInstanceId)) return false;

        var match = HardwareIdPattern.Match(hardwareInstanceId);
        if (!match.Success ||
            !ushort.TryParse(
                match.Groups["vendor"].Value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var vendorId) ||
            !ushort.TryParse(
                match.Groups["product"].Value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var productId))
        {
            return false;
        }

        identity = new BluetoothAudioDeviceIdentity(vendorId, productId);
        return true;
    }
}
