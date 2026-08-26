namespace PerDeviceMixer.Bluetooth;

public static class AppleHeadphoneIds
{
    public const ushort CompanyId = 0x004C;
    public const ushort BeatsFitProProductId = 0x2012;
}

public enum HeadphoneSide
{
    Left,
    Right
}

public sealed record HeadphoneBatteryState(
    ushort ProductId,
    string ModelName,
    int? LeftBatteryPercent,
    int? RightBatteryPercent,
    int? CaseBatteryPercent,
    bool LeftCharging,
    bool RightCharging,
    bool CaseCharging,
    HeadphoneSide BroadcastingSide,
    short SignalStrengthDbm,
    DateTimeOffset ObservedAtUtc);

public sealed class HeadphoneBatteryStateChangedEventArgs(
    HeadphoneBatteryState? state) : EventArgs
{
    public HeadphoneBatteryState? State { get; } = state;
}

public sealed class HeadphoneMonitorErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

public interface IHeadphoneBatteryMonitor : IDisposable
{
    event EventHandler<HeadphoneBatteryStateChangedEventArgs>? StateChanged;
    event EventHandler<HeadphoneMonitorErrorEventArgs>? MonitoringFailed;

    bool IsRunning { get; }
    HeadphoneBatteryState? CurrentState { get; }

    void Start(ushort productId);
    void StopMonitoring();
}
