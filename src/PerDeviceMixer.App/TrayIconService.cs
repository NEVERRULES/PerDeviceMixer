using System.Drawing;
using System.Windows.Forms;

namespace PerDeviceMixer.App;

internal sealed class TrayIconService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _muteItem = new();
    private readonly ToolStripMenuItem _outputDevicesItem = new("输出设备");
    private Icon? _icon;

    public TrayIconService(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "PerDeviceMixer",
            Visible = true,
            ContextMenuStrip = _menu
        };

        var openItem = new ToolStripMenuItem("打开音量控制");
        openItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        _muteItem.Click += (_, _) => _viewModel.ToggleMasterMute();
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu.Items.Add(openItem);
        _menu.Items.Add(_muteItem);
        _menu.Items.Add(_outputDevicesItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);
        _menu.Opening += (_, _) => RefreshMenu();
        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                ShowRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    private void RefreshMenu()
    {
        _muteItem.Text = _viewModel.MasterMuted ? "取消静音" : "静音";
        _outputDevicesItem.DropDownItems.Clear();

        foreach (var device in _viewModel.OutputDevices)
        {
            var item = new ToolStripMenuItem(device.Name)
            {
                Checked = device.IsDefault,
                Enabled = !device.IsDefault
            };
            item.Click += (_, _) => _viewModel.SetOutputDevice(device);
            _outputDevicesItem.DropDownItems.Add(item);
        }

        if (_outputDevicesItem.DropDownItems.Count == 0)
        {
            _outputDevicesItem.DropDownItems.Add(new ToolStripMenuItem("没有可用设备")
            {
                Enabled = false
            });
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
        _icon = null;
    }
}
