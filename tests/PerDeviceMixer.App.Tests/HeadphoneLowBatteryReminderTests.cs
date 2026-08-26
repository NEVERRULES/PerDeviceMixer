using PerDeviceMixer.App;
using PerDeviceMixer.Bluetooth;

namespace PerDeviceMixer.App.Tests;

public sealed class HeadphoneLowBatteryReminderTests
{
    [Fact]
    public void NotifiesOnceWhenAvailableComponentReachesTwentyPercent()
    {
        var reminder = new HeadphoneLowBatteryReminder();
        var status = CreateStatus(left: 20, right: 60, @case: null);

        var first = reminder.Evaluate(status);
        var repeated = reminder.Evaluate(status);

        Assert.NotNull(first);
        Assert.Equal("Beats Fit Pro 电量低", first.Title);
        Assert.Equal("左耳仅剩 20%，请及时充电。", first.Message);
        Assert.Null(repeated);
    }

    [Fact]
    public void RearmsAfterBatteryRecoversAboveThreshold()
    {
        var reminder = new HeadphoneLowBatteryReminder();

        Assert.NotNull(reminder.Evaluate(CreateStatus(left: 20)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 30)));
        Assert.NotNull(reminder.Evaluate(CreateStatus(left: 20)));
    }

    [Fact]
    public void DoesNotNotifyUnavailableOrChargingComponents()
    {
        var reminder = new HeadphoneLowBatteryReminder();

        Assert.Null(reminder.Evaluate(CreateStatus(left: null, @case: 20, caseCharging: true)));
    }

    [Fact]
    public void DisconnectRearmsLowBatteryReminder()
    {
        var reminder = new HeadphoneLowBatteryReminder();

        Assert.NotNull(reminder.Evaluate(CreateStatus(right: 10)));
        Assert.Null(reminder.Evaluate(HeadphoneBatteryStatus.Inactive));
        Assert.NotNull(reminder.Evaluate(CreateStatus(right: 10)));
    }

    private static HeadphoneBatteryStatus CreateStatus(
        int? left = 80,
        int? right = 80,
        int? @case = 80,
        bool leftCharging = false,
        bool rightCharging = false,
        bool caseCharging = false) =>
        new(
            true,
            "Beats Fit Pro",
            new HeadphoneBatteryState(
                AppleHeadphoneIds.BeatsFitProProductId,
                "Beats Fit Pro",
                left,
                right,
                @case,
                leftCharging,
                rightCharging,
                caseCharging,
                HeadphoneSide.Left,
                -40,
                DateTimeOffset.UnixEpoch),
            null);
}
