using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

/// <summary>
/// A short-lived, click-through toast at the bottom center of the primary
/// work area. It appears whenever the default output device changes and shows
/// the device name plus its master volume.
/// </summary>
public sealed class DeviceSwitchToast : IDisposable
{
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan BatteryWaitDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(250);
    private const double BottomMargin = 24;

    private DeviceSwitchToastWindow? _window;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool IsVisible => _window?.IsVisible == true;
    internal event EventHandler? Dismissed;

    private readonly MixerEngine _engine;
    private readonly HeadphoneBatteryCoordinator _headphoneBattery;
    private readonly DispatcherTimer _displayTimer;
    private readonly DispatcherTimer _lifetimeTimer;
    private string? _lastShownDeviceId;
    private bool _waitingForBattery;
    private bool _fadingOut;
    private bool _disposed;

    internal DeviceSwitchToast(
        MixerEngine engine,
        HeadphoneBatteryCoordinator headphoneBattery)
    {
        _engine = engine;
        _headphoneBattery = headphoneBattery;
        _displayTimer = new DispatcherTimer(
            DisplayDuration,
            DispatcherPriority.Background,
            OnDisplayTimerTick,
            _dispatcher);
        _displayTimer.Stop();
        _lifetimeTimer = new DispatcherTimer(
            MaximumLifetime,
            DispatcherPriority.Background,
            OnLifetimeTimerTick,
            _dispatcher);
        _lifetimeTimer.Stop();
        _lastShownDeviceId = GetCurrentSnapshot()?.Endpoint.Id;
        _engine.MixerChanged += OnMixerChanged;
        _headphoneBattery.StatusChanged += OnHeadphoneBatteryStatusChanged;
    }

    internal static string FormatDeviceName(MixerSnapshot? snapshot)
    {
        if (snapshot is null) return "暂无可用输出设备";
        var deviceName = snapshot.Endpoint.Name
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return string.IsNullOrWhiteSpace(deviceName) ? "未知输出设备" : deviceName;
    }

    internal static string FormatVolumeText(MixerSnapshot? snapshot)
    {
        if (snapshot is null) return string.Empty;
        var volume = (int)Math.Round(
            Math.Clamp(snapshot.Endpoint.MasterVolume, 0f, 1f) * 100d,
            MidpointRounding.AwayFromZero);
        return snapshot.Endpoint.IsMuted
            ? $"主音量 {volume}% · 已静音"
            : $"主音量 {volume}%";
    }

    internal static bool ShouldShow(string? deviceId, string? lastShownDeviceId) =>
        !string.IsNullOrWhiteSpace(deviceId) &&
        !string.Equals(deviceId, lastShownDeviceId, StringComparison.OrdinalIgnoreCase);

    internal static string FormatBatteryText(HeadphoneBatteryStatus status)
    {
        if (!status.IsSupportedDeviceActive || status.Battery is null) return string.Empty;
        var battery = status.Battery;
        return $"左 {FormatBatteryValue(battery.LeftBatteryPercent, battery.LeftCharging)} · " +
               $"右 {FormatBatteryValue(battery.RightBatteryPercent, battery.RightCharging)} · " +
               $"充电盒 {FormatBatteryValue(battery.CaseBatteryPercent, battery.CaseCharging)}";
    }

    internal static string FormatBatteryToastText(
        AudioEndpointInfo endpoint,
        HeadphoneBatteryStatus status,
        bool batteryDisplayEnabled)
    {
        if (!batteryDisplayEnabled || !HeadphoneBatteryCoordinator.IsSupportedEndpoint(endpoint))
        {
            return string.Empty;
        }

        if (status.Battery is not null) return FormatBatteryText(status);
        return status.ErrorMessage is null
            ? "正在读取耳机电量…"
            : "暂时无法读取耳机电量";
    }

    internal static bool ShouldWaitForBattery(
        AudioEndpointInfo endpoint,
        HeadphoneBatteryStatus status,
        bool batteryDisplayEnabled) =>
        batteryDisplayEnabled &&
        HeadphoneBatteryCoordinator.IsSupportedEndpoint(endpoint) &&
        status.Battery is null &&
        status.ErrorMessage is null;

    private static string FormatBatteryValue(int? percent, bool charging)
    {
        var value = percent.HasValue ? $"{percent.Value}%" : "--";
        return percent.HasValue && charging ? value + " ⚡" : value;
    }

    private MixerSnapshot? GetCurrentSnapshot()
    {
        try
        {
            return _engine.GetCurrentSnapshot();
        }
        catch
        {
            return null;
        }
    }

    private void OnMixerChanged(object? sender, AudioStateChangedEventArgs eventArgs)
    {
        if (eventArgs.Kind is not (
                AudioChangeKind.DefaultDevice or
                AudioChangeKind.MasterVolume)) return;
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _ = _dispatcher.BeginInvoke(() => HandleOnUiThread(eventArgs));
    }

    private void OnHeadphoneBatteryStatusChanged(
        object? sender,
        HeadphoneBatteryStatusChangedEventArgs eventArgs)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _ = _dispatcher.BeginInvoke(() => HandleBatteryOnUiThread(eventArgs.Status));
    }

    private void HandleBatteryOnUiThread(HeadphoneBatteryStatus status)
    {
        if (_disposed || !IsVisible || !IsEnabledBySettings()) return;
        var snapshot = GetCurrentSnapshot();
        if (snapshot is null ||
            !status.IsSupportedDeviceActive ||
            !string.Equals(
                snapshot.Endpoint.Id,
                _lastShownDeviceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsVisible)
        {
            _fadingOut = false;
            _window?.BeginAnimation(UIElement.OpacityProperty, null);
            if (_window is not null) _window.Opacity = 1;
            UpdateText(snapshot);
            PositionAtBottomCenter();
            _displayTimer.Stop();
            _displayTimer.Start();
            if (!_waitingForBattery)
            {
                _lifetimeTimer.Stop();
            }
        }
    }

    private void HandleOnUiThread(AudioStateChangedEventArgs eventArgs)
    {
        if (_disposed) return;
        if (!IsEnabledBySettings())
        {
            HideToast();
            return;
        }

        if (eventArgs.Kind == AudioChangeKind.DefaultDevice)
        {
            var snapshot = GetCurrentSnapshot();
            if (snapshot is null) return;
            if (!ShouldShow(snapshot.Endpoint.Id, _lastShownDeviceId)) return;
            ShowToast(snapshot);
        }
        else if (eventArgs.Kind == AudioChangeKind.MasterVolume && IsVisible)
        {
            var snapshot = GetCurrentSnapshot();
            if (snapshot is null) return;
            UpdateText(snapshot);
        }
    }

    private bool IsEnabledBySettings()
    {
        try
        {
            return _engine.Profiles.Settings.ShowDeviceSwitchToast;
        }
        catch
        {
            return true;
        }
    }

    private bool IsBatteryDisplayEnabled()
    {
        try
        {
            return _engine.Profiles.Settings.ShowSupportedHeadphoneBattery;
        }
        catch
        {
            return true;
        }
    }

    private void HideToast()
    {
        _fadingOut = false;
        _waitingForBattery = false;
        _window?.BeginAnimation(UIElement.OpacityProperty, null);
        _displayTimer.Stop();
        _lifetimeTimer.Stop();
        var window = _window;
        _window = null;
        if (window is null) return;
        if (ReferenceEquals(Application.Current?.MainWindow, window))
        {
            Application.Current.MainWindow = null;
        }
        window.Close();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    internal void ShowToast(MixerSnapshot snapshot)
    {
        if (_disposed) return;
        _fadingOut = false;
        UpdateText(snapshot);
        _lastShownDeviceId = snapshot.Endpoint.Id;
        if (!IsVisible)
        {
            _window = new DeviceSwitchToastWindow();
            UpdateText(snapshot);
            _window.Opacity = 0;
            _window.Show();
            _window.BeginAnimation(
                UIElement.OpacityProperty,
                new DoubleAnimation(0d, 1d, FadeInDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            _window?.BeginAnimation(UIElement.OpacityProperty, null);
            _window!.Opacity = 1;
        }

        _window!.UpdateLayout();
        PositionAtBottomCenter();
        _displayTimer.Stop();
        _displayTimer.Start();
        _lifetimeTimer.Stop();
        _lifetimeTimer.Interval = _waitingForBattery
            ? BatteryWaitDuration
            : MaximumLifetime;
        _lifetimeTimer.Start();
    }

    private void UpdateText(MixerSnapshot snapshot)
    {
        if (_window is null) return;
        _window.DeviceNameText.Text = FormatDeviceName(snapshot);
        _window.VolumeText.Text = FormatVolumeText(snapshot);
        var batteryStatus = _headphoneBattery.CurrentStatus;
        var batteryDisplayEnabled = IsBatteryDisplayEnabled();
        var batteryText = FormatBatteryToastText(
            snapshot.Endpoint,
            batteryStatus,
            batteryDisplayEnabled);
        _waitingForBattery = ShouldWaitForBattery(
            snapshot.Endpoint,
            batteryStatus,
            batteryDisplayEnabled);
        _window.BatteryText.Text = batteryText;
        _window.BatteryText.Visibility = string.IsNullOrWhiteSpace(batteryText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PositionAtBottomCenter()
    {
        if (_window is null) return;
        var area = SystemParameters.WorkArea;
        _window.Left = area.Left + Math.Max(0, (area.Width - _window.ActualWidth) / 2);
        _window.Top = area.Bottom - _window.ActualHeight - BottomMargin;
    }

    private void OnDisplayTimerTick(object? sender, EventArgs eventArgs)
    {
        _displayTimer.Stop();
        if (!IsVisible) return;
        if (_waitingForBattery) return;
        StartFadeOut();
    }

    private void OnLifetimeTimerTick(object? sender, EventArgs eventArgs)
    {
        _lifetimeTimer.Stop();
        _displayTimer.Stop();
        if (!IsVisible) return;
        _waitingForBattery = false;
        StartFadeOut();
    }

    private void StartFadeOut()
    {
        if (_fadingOut) return;
        _fadingOut = true;
        var animation = new DoubleAnimation(1d, 0d, FadeOutDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (!_fadingOut) return;
            _fadingOut = false;
            _lifetimeTimer.Stop();
            HideToast();
        };
        _window?.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.MixerChanged -= OnMixerChanged;
        _headphoneBattery.StatusChanged -= OnHeadphoneBatteryStatusChanged;
        _displayTimer.Stop();
        _displayTimer.Tick -= OnDisplayTimerTick;
        _lifetimeTimer.Stop();
        _lifetimeTimer.Tick -= OnLifetimeTimerTick;
        HideToast();
        GC.SuppressFinalize(this);
    }
}
