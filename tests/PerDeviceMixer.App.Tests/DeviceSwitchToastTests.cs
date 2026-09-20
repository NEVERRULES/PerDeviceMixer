using PerDeviceMixer.App;
using PerDeviceMixer.Bluetooth;
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

    [Fact]
    public void FormatBatteryTextShowsAllComponentsAndCharging()
    {
        var status = new HeadphoneBatteryStatus(
            true,
            "Beats Fit Pro",
            new HeadphoneBatteryState(
                AppleHeadphoneIds.BeatsFitProProductId,
                "Beats Fit Pro",
                80,
                null,
                60,
                true,
                false,
                true,
                HeadphoneSide.Left,
                -40,
                DateTimeOffset.UnixEpoch),
            null);

        Assert.Equal(
            "左 80% ⚡ · 右 -- · 充电盒 60% ⚡",
            DeviceSwitchToast.FormatBatteryText(status));
    }

    [Fact]
    public void FormatBatteryTextIsEmptyForInactiveDevice()
    {
        Assert.Equal(
            string.Empty,
            DeviceSwitchToast.FormatBatteryText(HeadphoneBatteryStatus.Inactive));
    }

    [Fact]
    public void FormatBatteryTextDoesNotShowChargingForUnavailableComponent()
    {
        var status = new HeadphoneBatteryStatus(
            true,
            "Beats Fit Pro",
            new HeadphoneBatteryState(
                AppleHeadphoneIds.BeatsFitProProductId,
                "Beats Fit Pro",
                80,
                80,
                null,
                false,
                false,
                true,
                HeadphoneSide.Left,
                -40,
                DateTimeOffset.UnixEpoch),
            null);

        Assert.Equal(
            "左 80% · 右 80% · 充电盒 --",
            DeviceSwitchToast.FormatBatteryText(status));
    }

    [Fact]
    public void SupportedEndpointShowsWaitingStateUntilBatteryArrives()
    {
        var endpoint = CreateSupportedEndpoint();

        Assert.Equal(
            "正在读取耳机电量…",
            DeviceSwitchToast.FormatBatteryToastText(
                endpoint,
                HeadphoneBatteryStatus.Inactive,
                batteryDisplayEnabled: true));
        Assert.True(DeviceSwitchToast.ShouldWaitForBattery(
            endpoint,
            HeadphoneBatteryStatus.Inactive,
            batteryDisplayEnabled: true));
    }

    [Fact]
    public void SupportedEndpointStopsWaitingWhenBatteryReadFails()
    {
        var endpoint = CreateSupportedEndpoint();
        var status = new HeadphoneBatteryStatus(
            true,
            "Beats Fit Pro",
            null,
            "MonitorFailure");

        Assert.Equal(
            "暂时无法读取耳机电量",
            DeviceSwitchToast.FormatBatteryToastText(
                endpoint,
                status,
                batteryDisplayEnabled: true));
        Assert.False(DeviceSwitchToast.ShouldWaitForBattery(
            endpoint,
            status,
            batteryDisplayEnabled: true));
    }

    [Fact]
    public void DisabledBatteryDisplayDoesNotShowWaitingState()
    {
        var endpoint = CreateSupportedEndpoint();

        Assert.Equal(
            string.Empty,
            DeviceSwitchToast.FormatBatteryToastText(
                endpoint,
                HeadphoneBatteryStatus.Inactive,
                batteryDisplayEnabled: false));
        Assert.False(DeviceSwitchToast.ShouldWaitForBattery(
            endpoint,
            HeadphoneBatteryStatus.Inactive,
            batteryDisplayEnabled: false));
    }

    [Fact]
    public void SupportedEndpointUsesHardwareIdentityInsteadOfDisplayName()
    {
        var endpoint = new AudioEndpointInfo(
            "device",
            "用户自定义名称",
            true,
            0.2f,
            false,
            "{1}.BTHENUM\\service_VID&0001004C_PID&2012\\device");

        Assert.True(HeadphoneBatteryCoordinator.IsSupportedEndpoint(endpoint));
    }

    private static MixerSnapshot CreateSnapshot(string deviceName, float volume, bool muted) =>
        new(
            new AudioEndpointInfo("device", deviceName, true, volume, muted),
            []);

    private static AudioEndpointInfo CreateSupportedEndpoint() =>
        new(
            "device",
            "Beats Fit Pro",
            true,
            0.2f,
            false,
            "{1}.BTHENUM\\service_VID&0001004C_PID&2012\\device");
}
