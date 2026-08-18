using PerDeviceMixer.App;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App.Tests;

public sealed class DeviceSwitchToastTests
{
    [Fact]
    public void FormatDeviceNameShowsDeviceName()
    {
        var snapshot = CreateSnapshot("蓝牙耳机", 0.26f, muted: false);

        var deviceName = DeviceSwitchToast.FormatDeviceName(snapshot);

        Assert.Equal("蓝牙耳机", deviceName);
    }

    [Fact]
    public void FormatDeviceNameCleansLineBreaks()
    {
        var snapshot = CreateSnapshot("耳机\r\n(蓝牙)", 0.5f, muted: false);

        var deviceName = DeviceSwitchToast.FormatDeviceName(snapshot);

        Assert.Equal("耳机  (蓝牙)", deviceName);
    }

    [Fact]
    public void FormatDeviceNameFallsBackWhenNameIsBlank()
    {
        var snapshot = CreateSnapshot("   ", 0.5f, muted: false);

        var deviceName = DeviceSwitchToast.FormatDeviceName(snapshot);

        Assert.Equal("未知输出设备", deviceName);
    }

    [Fact]
    public void FormatDeviceNameExplainsWhenNoOutputDeviceIsAvailable()
    {
        Assert.Equal("暂无可用输出设备", DeviceSwitchToast.FormatDeviceName(null));
    }

    [Fact]
    public void FormatVolumeTextShowsMasterVolume()
    {
        var snapshot = CreateSnapshot("扬声器", 0.42f, muted: false);

        var volumeText = DeviceSwitchToast.FormatVolumeText(snapshot);

        Assert.Equal("主音量 42%", volumeText);
    }

    [Fact]
    public void FormatVolumeTextShowsMutedState()
    {
        var snapshot = CreateSnapshot("耳机", 0.08f, muted: true);

        var volumeText = DeviceSwitchToast.FormatVolumeText(snapshot);

        Assert.Equal("主音量 8% · 已静音", volumeText);
    }

    [Fact]
    public void FormatVolumeTextIsEmptyWithoutSnapshot()
    {
        Assert.Equal(string.Empty, DeviceSwitchToast.FormatVolumeText(null));
    }

    [Theory]
    [InlineData("bluetooth-headphones", "wired-headphones")]
    [InlineData("bluetooth-headphones", null)]
    public void ShouldShowIsTrueWhenDeviceDiffers(string? deviceId, string? lastShownDeviceId)
    {
        Assert.True(DeviceSwitchToast.ShouldShow(deviceId, lastShownDeviceId));
    }

    [Fact]
    public void ShouldShowIsFalseForSameDevice()
    {
        Assert.False(DeviceSwitchToast.ShouldShow("headphones", "headphones"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldShowIsFalseForMissingDeviceId(string? deviceId)
    {
        Assert.False(DeviceSwitchToast.ShouldShow(deviceId, "headphones"));
    }

    private static MixerSnapshot CreateSnapshot(string deviceName, float volume, bool muted) =>
        new(
            new AudioEndpointInfo("device", deviceName, true, volume, muted),
            []);
}
