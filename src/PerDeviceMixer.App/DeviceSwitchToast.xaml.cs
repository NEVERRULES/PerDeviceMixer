using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

/// <summary>
/// A short-lived, click-through toast at the bottom center of the primary
/// work area. It appears whenever the default output device changes and shows
/// the device name plus its master volume.
/// </summary>
public partial class DeviceSwitchToast : Window, IDisposable
{
    private const int WindowLongExtendedStyle = -20;
    private const int ExtendedStyleNoActivate = 0x08000000;
    private const int ExtendedStyleTransparent = 0x00000020;
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(250);
    private const double BottomMargin = 24;

    private readonly MixerEngine _engine;
    private readonly HeadphoneBatteryCoordinator _headphoneBattery;
    private readonly DispatcherTimer _displayTimer;
    private readonly DispatcherTimer _lifetimeTimer;
    private string? _lastShownDeviceId;
    private DateTimeOffset _lastDeviceChangeUtc;
    private bool _fadingOut;
    private bool _disposed;

    internal DeviceSwitchToast(
        MixerEngine engine,
        HeadphoneBatteryCoordinator headphoneBattery)
    {
        _engine = engine;
        _headphoneBattery = headphoneBattery;
        InitializeComponent();
        _displayTimer = new DispatcherTimer(
            DisplayDuration,
            DispatcherPriority.Background,
            OnDisplayTimerTick,
            Dispatcher);
        _displayTimer.Stop();
        _lifetimeTimer = new DispatcherTimer(
            MaximumLifetime,
            DispatcherPriority.Background,
            OnLifetimeTimerTick,
            Dispatcher);
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
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() => HandleOnUiThread(eventArgs));
    }

    private void OnHeadphoneBatteryStatusChanged(
        object? sender,
        HeadphoneBatteryStatusChangedEventArgs eventArgs)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() => HandleBatteryOnUiThread(eventArgs.Status));
    }

    private void HandleBatteryOnUiThread(HeadphoneBatteryStatus status)
    {
        if (_disposed || !IsEnabledBySettings() || status.Battery is null) return;
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
            UpdateText(snapshot);
            PositionAtBottomCenter();
            _displayTimer.Stop();
            _displayTimer.Start();
        }
        else if (DateTimeOffset.UtcNow - _lastDeviceChangeUtc <= MaximumLifetime)
        {
            ShowToast(snapshot);
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

    private void HideToast()
    {
        _fadingOut = false;
        BeginAnimation(OpacityProperty, null);
        _displayTimer.Stop();
        _lifetimeTimer.Stop();
        if (IsVisible) Hide();
    }

    private void ShowToast(MixerSnapshot snapshot)
    {
        _fadingOut = false;
        UpdateText(snapshot);
        _lastShownDeviceId = snapshot.Endpoint.Id;
        _lastDeviceChangeUtc = DateTimeOffset.UtcNow;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0d, 1d, FadeInDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }
        else
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }

        UpdateLayout();
        PositionAtBottomCenter();
        _displayTimer.Stop();
        _displayTimer.Start();
        if (!_lifetimeTimer.IsEnabled)
        {
            _lifetimeTimer.Start();
        }
    }

    private void UpdateText(MixerSnapshot snapshot)
    {
        DeviceNameText.Text = FormatDeviceName(snapshot);
        VolumeText.Text = FormatVolumeText(snapshot);
        var batteryText = FormatBatteryText(_headphoneBattery.CurrentStatus);
        BatteryText.Text = batteryText;
        BatteryText.Visibility = string.IsNullOrWhiteSpace(batteryText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PositionAtBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
        Top = area.Bottom - ActualHeight - BottomMargin;
    }

    private void OnDisplayTimerTick(object? sender, EventArgs eventArgs)
    {
        _displayTimer.Stop();
        if (!IsVisible) return;
        StartFadeOut();
    }

    private void OnLifetimeTimerTick(object? sender, EventArgs eventArgs)
    {
        _lifetimeTimer.Stop();
        _displayTimer.Stop();
        if (!IsVisible) return;
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
            Hide();
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var extendedStyle = GetWindowLongPtr(handle, WindowLongExtendedStyle).ToInt64();
        extendedStyle |= ExtendedStyleNoActivate | ExtendedStyleTransparent;
        _ = SetWindowLongPtr(handle, WindowLongExtendedStyle, new IntPtr(extendedStyle));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MakeClickThrough();
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
        Close();
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
