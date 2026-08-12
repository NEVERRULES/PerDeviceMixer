using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace PerDeviceMixer.App;

internal static class BackgroundMemoryOptimizer
{
    public static void TrimCurrentProcess()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        using var process = Process.GetCurrentProcess();
        _ = EmptyWorkingSet(process.Handle);
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(nint processHandle);
}
