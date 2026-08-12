using PerDeviceMixer.App;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App.Tests;

public sealed class UpdateCoordinatorTests
{
    [Fact]
    public void NextAutomaticCheckUsesTheConfiguredSuccessInterval()
    {
        var lastSuccess = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var settings = new MixerSettings
        {
            UpdateCheckIntervalHours = 72,
            LastSuccessfulUpdateCheckUtc = lastSuccess,
            LastUpdateAttemptUtc = lastSuccess
        };

        var due = UpdateCoordinator.CalculateNextAutomaticCheck(settings);

        Assert.Equal(lastSuccess.AddHours(72), due);
    }

    [Fact]
    public void FailedAttemptBackoffPreventsRetryForOneHour()
    {
        var attempt = new DateTimeOffset(2026, 8, 12, 10, 30, 0, TimeSpan.Zero);
        var settings = new MixerSettings
        {
            UpdateCheckIntervalHours = 24,
            LastSuccessfulUpdateCheckUtc = attempt.AddDays(-2),
            LastUpdateAttemptUtc = attempt
        };

        var due = UpdateCoordinator.CalculateNextAutomaticCheck(settings);

        Assert.Equal(attempt.AddHours(1), due);
    }
}
