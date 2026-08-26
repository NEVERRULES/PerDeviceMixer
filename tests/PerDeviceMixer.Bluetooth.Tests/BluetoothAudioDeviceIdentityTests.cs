using PerDeviceMixer.Bluetooth;

namespace PerDeviceMixer.Bluetooth.Tests;

public sealed class BluetoothAudioDeviceIdentityTests
{
    [Theory]
    [InlineData("{1}.BTHENUM\\{0000110B-0000-1000-8000-00805F9B34FB}_VID&0001004C_PID&2012\\device")]
    [InlineData("BTHENUM\\service_VID&004C_PID&2012\\device")]
    public void RecognizesBeatsFitProHardwareIdentity(string instanceId)
    {
        Assert.True(BluetoothAudioDeviceIdentity.TryParse(instanceId, out var identity));
        Assert.NotNull(identity);
        Assert.True(identity.IsBeatsFitPro);
        Assert.Equal(0x004C, identity.VendorId);
        Assert.Equal(0x2012, identity.ProductId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SWD\\MMDEVAPI\\endpoint")]
    [InlineData("BTHENUM\\service_VID&0001004C_PID&2014\\device")]
    public void DoesNotRecognizeUnsupportedHardware(string? instanceId)
    {
        var parsed = BluetoothAudioDeviceIdentity.TryParse(instanceId, out var identity);

        Assert.False(parsed && identity?.IsBeatsFitPro == true);
    }
}
