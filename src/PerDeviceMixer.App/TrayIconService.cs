using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

internal sealed class TrayIconService : IDisposable
{
    private const int CallbackMessage = 0x8001;
    private const int WindowMessageLeftButtonUp = 0x0202;
    private const int WindowMessageRightButtonUp = 0x0205;
    private const int WindowMessageContextMenu = 0x007B;
    private const int NotifyIconSelect = 0x0400;
    private const int NotifyIconKeySelect = 0x0401;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconSetVersion = 0x00000004;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint NotifyIconInfo = 0x00000010;
    private const uint NotifyIconShowTip = 0x00000080;
    private const uint NotifyIconVersion4 = 4;
    private const uint NotifyIconInfoFlag = 0x00000001;
    private const int MaximumToolTipLength = 127;

    private readonly MixerEngine _engine;
    private readonly HwndSource _messageWindow;
    private readonly uint _taskbarCreatedMessage;
    private ContextMenu? _menu;
    private MenuItem? _muteItem;
    private MenuItem? _outputDevicesItem;
    private MenuItem? _checkUpdatesItem;
    private string? _updateVersion;
    private string _toolTip = "PerDeviceMixer";
    private Icon? _icon;
    private int _toolTipRefreshPending;
    private bool _disposed;

    public TrayIconService(MixerEngine engine)
    {
        _engine = engine;
        _messageWindow = new HwndSource(new HwndSourceParameters("PerDeviceMixer.TrayMessages")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _messageWindow.AddHook(WindowProcedure);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
            ?? (Icon)SystemIcons.Application.Clone();

        _engine.MixerChanged += OnMixerChanged;
        _toolTip = GetCurrentToolTip();
        AddIcon();
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? CheckUpdatesRequested;
    public event EventHandler? FeedbackRequested;
    public event EventHandler? ProjectRequested;
    public event EventHandler? MenuClosed;

    public void SetUpdateAvailable(string? version)
    {
        _updateVersion = version;
        if (_checkUpdatesItem is null) return;
        _checkUpdatesItem.Header = string.IsNullOrWhiteSpace(version)
            ? "检查更新"
            : $"发现新版本 {version}";
    }

    public void ShowUpdateNotification(string version)
    {
        if (_disposed || _icon is null) return;
        var data = CreateNotifyIconData();
        data.Flags = NotifyIconInfo;
        data.InfoTitle = "PerDeviceMixer 有新版本";
        data.Info = $"版本 {version} 已发布。点击托盘菜单可查看更新。";
        data.InfoFlags = NotifyIconInfoFlag;
        _ = ShellNotifyIcon(NotifyIconModify, ref data);
    }

    private static MenuItem CreateItem(string header, RoutedEventHandler click)
    {
        var item = new MenuItem { Header = header };
        item.Click += click;
        return item;
    }

    private void OnMenuOpened(object sender, RoutedEventArgs eventArgs)
    {
        if (_muteItem is null || _outputDevicesItem is null) return;
        try
        {
            var snapshot = _engine.GetCurrentSnapshot();
            _muteItem.Header = snapshot?.Endpoint.IsMuted == true ? "取消静音" : "静音";

            _outputDevicesItem.Items.Clear();
            foreach (var device in _engine.GetDevices())
            {
                var item = new MenuItem
                {
                    Header = device.Name,
                    IsCheckable = true,
                    IsChecked = device.IsDefault,
                    IsEnabled = !device.IsDefault,
                    Tag = device.Id
                };
                item.Click += OnOutputDeviceClick;
                _outputDevicesItem.Items.Add(item);
            }

            if (_outputDevicesItem.Items.Count == 0)
            {
                _outputDevicesItem.Items.Add(new MenuItem
                {
                    Header = "没有可用设备",
                    IsEnabled = false
                });
            }
        }
        catch
        {
            _outputDevicesItem.Items.Clear();
            _outputDevicesItem.Items.Add(new MenuItem
            {
                Header = "暂时无法读取设备",
                IsEnabled = false
            });
        }
    }

    private void OnOutputDeviceClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is MenuItem { Tag: string deviceId }) _engine.SetDefaultOutputDevice(deviceId);
    }

    private void OnMixerChanged(object? sender, AudioStateChangedEventArgs eventArgs)
    {
        if (eventArgs.Kind is not (
                AudioChangeKind.DefaultDevice or
                AudioChangeKind.MasterVolume or
                AudioChangeKind.DeviceCollection)) return;

        ScheduleToolTipRefresh();
    }

    private void ScheduleToolTipRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _toolTipRefreshPending, 1) != 0) return;
        var dispatcher = _messageWindow.Dispatcher;
        if (dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref _toolTipRefreshPending, 0);
            return;
        }

        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                Interlocked.Exchange(ref _toolTipRefreshPending, 0);
                RefreshToolTip();
            }));
    }

    private void RefreshToolTip()
    {
        if (_disposed) return;
        var toolTip = GetCurrentToolTip();
        if (string.Equals(_toolTip, toolTip, StringComparison.Ordinal)) return;

        _toolTip = toolTip;
        var data = CreateNotifyIconData();
        data.Flags = NotifyIconTip | NotifyIconShowTip;
        _ = ShellNotifyIcon(NotifyIconModify, ref data);
    }

    private string GetCurrentToolTip()
    {
        try
        {
            return FormatToolTip(_engine.GetCurrentSnapshot());
        }
        catch
        {
            return _toolTip;
        }
    }

    internal static string FormatToolTip(MixerSnapshot? snapshot)
    {
        if (snapshot is null) return "PerDeviceMixer\n暂无可用输出设备";

        var deviceName = snapshot.Endpoint.Name
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(deviceName)) deviceName = "未知输出设备";

        var volume = (int)Math.Round(
            Math.Clamp(snapshot.Endpoint.MasterVolume, 0f, 1f) * 100d,
            MidpointRounding.AwayFromZero);
        var status = snapshot.Endpoint.IsMuted
            ? $"主音量 {volume}% · 已静音"
            : $"主音量 {volume}%";
        var maximumDeviceNameLength = MaximumToolTipLength - status.Length - 1;
        if (deviceName.Length > maximumDeviceNameLength)
        {
            deviceName = deviceName[..(maximumDeviceNameLength - 1)] + "…";
        }

        return $"{deviceName}\n{status}";
    }

    private void ToggleMasterMute()
    {
        var snapshot = _engine.GetCurrentSnapshot();
        if (snapshot is null) return;
        _engine.SetMasterVolume(snapshot.Endpoint.MasterVolume, !snapshot.Endpoint.IsMuted);
    }

    private void OpenMenu()
    {
        if (_disposed) return;
        EnsureMenu();
        _ = SetForegroundWindow(_messageWindow.Handle);
        if (_menu is not null) _menu.IsOpen = true;
    }

    private void EnsureMenu()
    {
        if (_menu is not null) return;

        var resources = new ResourceDictionary
        {
            Source = new Uri("TrayMenuResources.xaml", UriKind.Relative)
        };
        var menu = new ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false
        };
        menu.Resources.MergedDictionaries.Add(resources);
        menu.Style = resources["TrayContextMenuStyle"] as Style;
        menu.Opened += OnMenuOpened;
        menu.Closed += OnMenuClosed;

        var openItem = CreateItem("打开音量控制", (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        _muteItem = CreateItem("静音", (_, _) => ToggleMasterMute());
        _outputDevicesItem = new MenuItem { Header = "输出设备" };
        _checkUpdatesItem = CreateItem(
            string.IsNullOrWhiteSpace(_updateVersion) ? "检查更新" : $"发现新版本 {_updateVersion}",
            (_, _) => CheckUpdatesRequested?.Invoke(this, EventArgs.Empty));
        var feedbackItem = CreateItem("问题反馈", (_, _) => FeedbackRequested?.Invoke(this, EventArgs.Empty));
        var projectItem = CreateItem("GitHub 项目主页", (_, _) => ProjectRequested?.Invoke(this, EventArgs.Empty));
        var exitItem = CreateItem("退出", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_muteItem);
        menu.Items.Add(_outputDevicesItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_checkUpdatesItem);
        menu.Items.Add(feedbackItem);
        menu.Items.Add(projectItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        _menu = menu;
    }

    private void OnMenuClosed(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not ContextMenu menu) return;
        menu.Opened -= OnMenuOpened;
        menu.Closed -= OnMenuClosed;
        menu.Items.Clear();
        menu.Resources.MergedDictionaries.Clear();
        menu.Resources.Clear();
        _menu = null;
        _muteItem = null;
        _outputDevicesItem = null;
        _checkUpdatesItem = null;
        MenuClosed?.Invoke(this, EventArgs.Empty);
    }

    private IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if ((uint)message == _taskbarCreatedMessage)
        {
            AddIcon();
            handled = true;
            return IntPtr.Zero;
        }

        if (message != CallbackMessage) return IntPtr.Zero;

        // NOTIFYICON_VERSION_4 packs the callback message into the low word and
        // the icon identifier into the high word of lParam.
        var mouseMessage = unchecked((int)(long)lParam) & 0xFFFF;
        if (mouseMessage is WindowMessageLeftButtonUp or NotifyIconSelect or NotifyIconKeySelect)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        else if (mouseMessage is WindowMessageRightButtonUp or WindowMessageContextMenu)
        {
            OpenMenu();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void AddIcon()
    {
        if (_disposed || _icon is null) return;
        var data = CreateNotifyIconData();
        data.Flags = NotifyIconMessage | NotifyIconIcon | NotifyIconTip | NotifyIconShowTip;
        if (!ShellNotifyIcon(NotifyIconAdd, ref data)) return;
        data.TimeoutOrVersion = NotifyIconVersion4;
        _ = ShellNotifyIcon(NotifyIconSetVersion, ref data);
    }

    private NotifyIconData CreateNotifyIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _messageWindow.Handle,
        Id = 1,
        CallbackMessage = CallbackMessage,
        IconHandle = _icon?.Handle ?? IntPtr.Zero,
        Tip = _toolTip,
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.MixerChanged -= OnMixerChanged;
        if (_menu is not null)
        {
            var menu = _menu;
            _menu = null;
            menu.Opened -= OnMenuOpened;
            menu.Closed -= OnMenuClosed;
            menu.IsOpen = false;
            menu.Items.Clear();
            menu.Resources.MergedDictionaries.Clear();
            menu.Resources.Clear();
            _muteItem = null;
            _outputDevicesItem = null;
            _checkUpdatesItem = null;
        }
        var data = CreateNotifyIconData();
        _ = ShellNotifyIcon(NotifyIconDelete, ref data);
        _messageWindow.RemoveHook(WindowProcedure);
        _messageWindow.Dispose();
        _icon?.Dispose();
        _icon = null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
