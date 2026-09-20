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
    public void PercentageFluctuationsDoNotRearmReminder()
    {
        var reminder = new HeadphoneLowBatteryReminder();

        Assert.NotNull(reminder.Evaluate(CreateStatus(left: 20)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 30)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 20)));
    }

    [Fact]
    public void DoesNotNotifyUnavailableOrChargingComponents()
    {
        var reminder = new HeadphoneLowBatteryReminder();

        Assert.Null(reminder.Evaluate(CreateStatus(left: null, @case: 20, caseCharging: true)));
    }

    [Fact]
    public void DisconnectDoesNotRearmLowBatteryReminder()
    {
        var reminder = new HeadphoneLowBatteryReminder();

        Assert.NotNull(reminder.Evaluate(CreateStatus(right: 10)));
        Assert.Null(reminder.Evaluate(HeadphoneBatteryStatus.Inactive));
        Assert.Null(reminder.Evaluate(CreateStatus(right: 10)));
    }

    [Fact]
    public void MissingBroadcastAndUnavailableComponentDoNotRearmReminder()
    {
        var reminder = new HeadphoneLowBatteryReminder();
        var status = CreateStatus(left: 20);
        Assert.NotNull(reminder.Evaluate(status));
        Assert.Null(reminder.Evaluate(status with { Battery = null }));
        Assert.Null(reminder.Evaluate(CreateStatus(left: null, leftCharging: true)));
        Assert.Null(reminder.Evaluate(status));
    }

    [Fact]
    public void OtherComponentsBecomingLowDoNotAddNotifications()
    {
        var reminder = new HeadphoneLowBatteryReminder();
        Assert.NotNull(reminder.Evaluate(CreateStatus(left: 20)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 10, right: 20)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 10, right: 10, @case: 20)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 10, caseCharging: true)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 10)));
    }

    [Fact]
    public void ChargingAlertedComponentRearmsEvenBelowThreshold()
    {
        var reminder = new HeadphoneLowBatteryReminder();
        Assert.NotNull(reminder.Evaluate(CreateStatus(left: 10)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 20, leftCharging: true)));
        Assert.Null(reminder.Evaluate(HeadphoneBatteryStatus.Inactive));
        Assert.NotNull(reminder.Evaluate(CreateStatus(left: 20)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 10)));
    }

    [Fact]
    public void AllAlertedComponentsMustChargeBeforeAnotherNotification()
    {
        var reminder = new HeadphoneLowBatteryReminder();
        var first = reminder.Evaluate(CreateStatus(left: 20, right: 10));
        Assert.Equal("左耳仅剩 20%，右耳仅剩 10%，请及时充电。", first?.Message);
        Assert.Null(reminder.Evaluate(CreateStatus(left: 20, right: 10, leftCharging: true)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 80, right: 10)));
        Assert.Null(reminder.Evaluate(CreateStatus(left: 80, right: 20, rightCharging: true)));
        Assert.NotNull(reminder.Evaluate(CreateStatus(left: 80, right: 20)));
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
