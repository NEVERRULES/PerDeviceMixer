using PerDeviceMixer.App;

namespace PerDeviceMixer.App.Tests;

public sealed class InstallerHandoffTests
{
    [Fact]
    public void LaunchFailureReacquiresMutexBeforeReturningTheError()
    {
        var released = false;
        var reacquired = false;

        var exception = Assert.Throws<InvalidOperationException>(() => InstallerHandoff.Launch(
            "setup.exe",
            () => released = true,
            () => reacquired = true,
            _ => throw new InvalidOperationException("launch failed")));

        Assert.Equal("launch failed", exception.Message);
        Assert.True(released);
        Assert.True(reacquired);
    }
}
