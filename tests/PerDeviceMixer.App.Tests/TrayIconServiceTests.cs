using PerDeviceMixer.App;
using PerDeviceMixer.Bluetooth;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App.Tests;

public sealed class TrayIconServiceTests
{
    [Fact]
    public void FormatToolTipShowsDeviceAndMasterVolume()
    {
        var snapshot = CreateSnapshot("扬声器 (Realtek(R) Audio)", 0.26f, muted: false);

        var toolTip = TrayIconService.FormatToolTip(snapshot);

        Assert.Equal("扬声器 (Realtek(R) Audio)\n主音量 26%", toolTip);
    }

    [Fact]
    public void FormatToolTipShowsMutedState()
    {
        var snapshot = CreateSnapshot("耳机", 0.08f, muted: true);

        var toolTip = TrayIconService.FormatToolTip(snapshot);

        Assert.Equal("耳机\n主音量 8% · 已静音", toolTip);
    }

    [Fact]
    public void FormatToolTipKeepsStatusWhenDeviceNameIsLong()
    {
        var snapshot = CreateSnapshot(new string('设', 160), 1f, muted: true);

        var toolTip = TrayIconService.FormatToolTip(snapshot);

        Assert.True(toolTip.Length <= 127);
        Assert.Contains("…\n主音量 100% · 已静音", toolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatToolTipExplainsWhenNoOutputDeviceIsAvailable()
    {
        Assert.Equal("PerDeviceMixer\n暂无可用输出设备", TrayIconService.FormatToolTip(null));
    }

    [Fact]
    public void FormatToolTipIncludesHeadphoneBatteryAndKeepsLengthLimit()
    {
        var snapshot = CreateSnapshot(new string('耳', 160), 0.12f, muted: false);
        var status = new HeadphoneBatteryStatus(
            true,
            "Beats Fit Pro",
            new HeadphoneBatteryState(
                AppleHeadphoneIds.BeatsFitProProductId,
                "Beats Fit Pro",
                80,
                70,
                60,
                false,
                true,
                false,
                HeadphoneSide.Left,
                -45,
                DateTimeOffset.UnixEpoch),
            null);

        var toolTip = TrayIconService.FormatToolTip(snapshot, status);

        Assert.True(toolTip.Length <= 127);
        Assert.Contains("主音量 12%", toolTip, StringComparison.Ordinal);
        Assert.Contains("左 80% · 右 70% ⚡ · 充电盒 60%", toolTip, StringComparison.Ordinal);
    }

    private static MixerSnapshot CreateSnapshot(string deviceName, float volume, bool muted) =>
        new(
            new AudioEndpointInfo("device", deviceName, true, volume, muted),
            []);
}
