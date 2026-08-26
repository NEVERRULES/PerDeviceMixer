using PerDeviceMixer.Bluetooth;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

internal sealed record HeadphoneBatteryStatus(
    bool IsSupportedDeviceActive,
    string DeviceName,
    HeadphoneBatteryState? Battery,
    string? ErrorMessage)
{
    public static HeadphoneBatteryStatus Inactive { get; } =
        new(false, string.Empty, null, null);
}

internal sealed class HeadphoneBatteryStatusChangedEventArgs(
    HeadphoneBatteryStatus status) : EventArgs
{
    public HeadphoneBatteryStatus Status { get; } = status;
}

internal sealed class HeadphoneBatteryCoordinator : IDisposable
{
    private readonly MixerEngine _engine;
    private readonly IHeadphoneBatteryMonitor _monitor;
    private readonly object _sync = new();
    private HeadphoneBatteryStatus _currentStatus = HeadphoneBatteryStatus.Inactive;
    private string? _activeEndpointId;
    private bool _started;
    private bool _disposed;

    public HeadphoneBatteryCoordinator(
        MixerEngine engine,
        IHeadphoneBatteryMonitor monitor)
    {
        _engine = engine;
        _monitor = monitor;
    }

    public event EventHandler<HeadphoneBatteryStatusChangedEventArgs>? StatusChanged;

    public HeadphoneBatteryStatus CurrentStatus
    {
        get
        {
            lock (_sync)
            {
                return _currentStatus;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
            _engine.MixerChanged += OnMixerChanged;
            _monitor.StateChanged += OnMonitorStateChanged;
            _monitor.MonitoringFailed += OnMonitoringFailed;
        }

        Reconcile();
    }

    public void Reconcile()
    {
        if (_disposed || !_started) return;

        MixerSnapshot? snapshot;
        bool enabled;
        try
        {
            enabled = _engine.GetSettingsSnapshot().ShowSupportedHeadphoneBattery;
            snapshot = _engine.GetCurrentSnapshot();
        }
        catch
        {
            enabled = false;
            snapshot = null;
        }

        var target = enabled && snapshot is not null && IsSupportedEndpoint(snapshot.Endpoint);
        if (!target)
        {
            Publish(HeadphoneBatteryStatus.Inactive, activeEndpointId: null);
            _monitor.StopMonitoring();
            return;
        }

        var endpointId = snapshot!.Endpoint.Id;
        var endpointChanged = false;
        lock (_sync)
        {
            endpointChanged = !string.Equals(
                _activeEndpointId,
                endpointId,
                StringComparison.OrdinalIgnoreCase);
        }

        var status = new HeadphoneBatteryStatus(
            true,
            snapshot.Endpoint.Name,
            endpointChanged ? null : _monitor.CurrentState,
            null);
        Publish(status, endpointId);

        if (endpointChanged) _monitor.StopMonitoring();

        if (_monitor.IsRunning) return;
        try
        {
            _monitor.Start(AppleHeadphoneIds.BeatsFitProProductId);
        }
        catch (Exception exception)
        {
            Publish(status with { ErrorMessage = exception.GetType().Name }, endpointId);
        }
    }

    internal static bool IsSupportedEndpoint(AudioEndpointInfo endpoint) =>
        BluetoothAudioDeviceIdentity.TryParse(endpoint.HardwareInstanceId, out var identity) &&
        identity?.IsBeatsFitPro == true;

    private void OnMixerChanged(object? sender, AudioStateChangedEventArgs eventArgs)
    {
        if (eventArgs.Kind is AudioChangeKind.DefaultDevice or AudioChangeKind.DeviceCollection)
        {
            Reconcile();
        }
    }

    private void OnMonitorStateChanged(
        object? sender,
        HeadphoneBatteryStateChangedEventArgs eventArgs)
    {
        HeadphoneBatteryStatus current;
        string? endpointId;
        lock (_sync)
        {
            if (_disposed || !_currentStatus.IsSupportedDeviceActive) return;
            current = _currentStatus;
            endpointId = _activeEndpointId;
        }

        Publish(current with { Battery = eventArgs.State, ErrorMessage = null }, endpointId);
    }

    private void OnMonitoringFailed(object? sender, HeadphoneMonitorErrorEventArgs eventArgs)
    {
        HeadphoneBatteryStatus current;
        string? endpointId;
        lock (_sync)
        {
            if (_disposed || !_currentStatus.IsSupportedDeviceActive) return;
            current = _currentStatus;
            endpointId = _activeEndpointId;
        }

        Publish(
            current with { ErrorMessage = eventArgs.Exception.GetType().Name },
            endpointId);
    }

    private void Publish(HeadphoneBatteryStatus status, string? activeEndpointId)
    {
        var changed = false;
        lock (_sync)
        {
            if (_disposed) return;
            _activeEndpointId = activeEndpointId;
            if (!Equals(_currentStatus, status))
            {
                _currentStatus = status;
                changed = true;
            }
        }

        if (changed)
        {
            StatusChanged?.Invoke(this, new HeadphoneBatteryStatusChangedEventArgs(status));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_started)
            {
                _engine.MixerChanged -= OnMixerChanged;
                _monitor.StateChanged -= OnMonitorStateChanged;
                _monitor.MonitoringFailed -= OnMonitoringFailed;
            }
        }

        _monitor.Dispose();
        GC.SuppressFinalize(this);
    }
}
