using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;
using Windows.Storage.Streams;

namespace PerDeviceMixer.Bluetooth;

public sealed class AppleHeadphoneBatteryMonitor : IHeadphoneBatteryMonitor
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromSeconds(10);
    private readonly object _sync = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private Timer? _expirationTimer;
    private HeadphoneBatteryState? _currentState;
    private ushort _targetProductId;
    private bool _disposed;

    public event EventHandler<HeadphoneBatteryStateChangedEventArgs>? StateChanged;
    public event EventHandler<HeadphoneMonitorErrorEventArgs>? MonitoringFailed;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _watcher is not null;
            }
        }
    }

    public HeadphoneBatteryState? CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _currentState;
            }
        }
    }

    public void Start(ushort productId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watcher is not null && _targetProductId == productId) return;
            StopCore(clearState: true);
            _targetProductId = productId;

            var filterData = new BluetoothLEManufacturerData
            {
                CompanyId = AppleHeadphoneIds.CompanyId
            };
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                // The Apple status is in the advertisement itself. Passive
                // scanning avoids requesting scan responses and uses less power.
                ScanningMode = BluetoothLEScanningMode.Passive
            };
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(filterData);
            watcher.Received += OnAdvertisementReceived;
            watcher.Stopped += OnWatcherStopped;
            _watcher = watcher;
            _expirationTimer = new Timer(
                OnExpirationTimer,
                null,
                StateLifetime,
                StateLifetime);

            try
            {
                watcher.Start();
            }
            catch
            {
                StopCore(clearState: true);
                throw;
            }
        }
    }

    public void StopMonitoring()
    {
        HeadphoneBatteryStateChangedEventArgs? changed = null;
        lock (_sync)
        {
            if (_disposed) return;
            if (_currentState is not null) changed = new(null);
            StopCore(clearState: true);
        }

        if (changed is not null) StateChanged?.Invoke(this, changed);
    }

    private void OnAdvertisementReceived(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs eventArgs)
    {
        try
        {
            foreach (var manufacturerData in eventArgs.Advertisement.ManufacturerData)
            {
                if (manufacturerData.CompanyId != AppleHeadphoneIds.CompanyId) continue;

                byte[] bytes;
                using (var reader = DataReader.FromBuffer(manufacturerData.Data))
                {
                    bytes = new byte[manufacturerData.Data.Length];
                    reader.ReadBytes(bytes);
                }

                if (!AppleProximityPairingParser.TryParse(
                        bytes,
                        eventArgs.RawSignalStrengthInDBm,
                        eventArgs.Timestamp,
                        out var parsed) ||
                    parsed is null ||
                    parsed.ProductId != _targetProductId)
                {
                    continue;
                }

                var accepted = false;
                lock (_sync)
                {
                    if (_disposed || !ReferenceEquals(sender, _watcher)) return;
                    if (!IsPlausibleContinuation(_currentState, parsed)) continue;
                    if (HasVisibleStateChanged(_currentState, parsed))
                    {
                        _currentState = parsed;
                        accepted = true;
                    }
                    else
                    {
                        _currentState = parsed;
                    }
                }

                if (accepted)
                {
                    StateChanged?.Invoke(this, new HeadphoneBatteryStateChangedEventArgs(parsed));
                }
            }
        }
        catch (Exception exception)
        {
            MonitoringFailed?.Invoke(this, new HeadphoneMonitorErrorEventArgs(exception));
        }
    }

    private void OnWatcherStopped(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementWatcherStoppedEventArgs eventArgs)
    {
        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _watcher)) return;
        }

        if (eventArgs.Error != BluetoothError.Success)
        {
            MonitoringFailed?.Invoke(
                this,
                new HeadphoneMonitorErrorEventArgs(
                    new InvalidOperationException($"Bluetooth scanning stopped: {eventArgs.Error}.")));
        }
    }

    private void OnExpirationTimer(object? state)
    {
        var expired = false;
        lock (_sync)
        {
            if (_disposed || _currentState is null) return;
            if (DateTimeOffset.UtcNow - _currentState.ObservedAtUtc < StateLifetime) return;
            _currentState = null;
            expired = true;
        }

        if (expired) StateChanged?.Invoke(this, new HeadphoneBatteryStateChangedEventArgs(null));
    }

    private static bool IsPlausibleContinuation(
        HeadphoneBatteryState? previous,
        HeadphoneBatteryState current)
    {
        if (previous is null || previous.ProductId != current.ProductId) return true;
        return BatteryDifferenceIsPlausible(previous.LeftBatteryPercent, current.LeftBatteryPercent) &&
               BatteryDifferenceIsPlausible(previous.RightBatteryPercent, current.RightBatteryPercent) &&
               BatteryDifferenceIsPlausible(previous.CaseBatteryPercent, current.CaseBatteryPercent) &&
               Math.Abs(previous.SignalStrengthDbm - current.SignalStrengthDbm) <= 50;
    }

    private static bool HasVisibleStateChanged(
        HeadphoneBatteryState? previous,
        HeadphoneBatteryState current) =>
        previous is null ||
        previous.ProductId != current.ProductId ||
        previous.LeftBatteryPercent != current.LeftBatteryPercent ||
        previous.RightBatteryPercent != current.RightBatteryPercent ||
        previous.CaseBatteryPercent != current.CaseBatteryPercent ||
        previous.LeftCharging != current.LeftCharging ||
        previous.RightCharging != current.RightCharging ||
        previous.CaseCharging != current.CaseCharging;

    private static bool BatteryDifferenceIsPlausible(int? previous, int? current) =>
        !previous.HasValue || !current.HasValue || Math.Abs(previous.Value - current.Value) <= 10;

    private void StopCore(bool clearState)
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is not null)
        {
            watcher.Received -= OnAdvertisementReceived;
            watcher.Stopped -= OnWatcherStopped;
            try
            {
                watcher.Stop();
            }
            catch (Exception)
            {
                // A radio can disappear between the status check and Stop.
            }
        }

        _expirationTimer?.Dispose();
        _expirationTimer = null;
        if (clearState) _currentState = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            StopCore(clearState: true);
        }

        GC.SuppressFinalize(this);
    }
}
