using PerDeviceMixer.App;
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

    private static MixerSnapshot CreateSnapshot(string deviceName, float volume, bool muted) =>
        new(
            new AudioEndpointInfo("device", deviceName, true, volume, muted),
            []);
}
