using System.Diagnostics;
using PerDeviceMixer.App;

namespace PerDeviceMixer.App.Tests;

public sealed class BackgroundMemoryOptimizerTests
{
    [Fact]
    public void TrimCurrentProcessKeepsTheProcessUsable()
    {
        var processId = Environment.ProcessId;

        BackgroundMemoryOptimizer.TrimCurrentProcess();

        using var process = Process.GetProcessById(processId);
        process.Refresh();
        Assert.False(process.HasExited);
        Assert.Equal(processId, process.Id);
    }
}
