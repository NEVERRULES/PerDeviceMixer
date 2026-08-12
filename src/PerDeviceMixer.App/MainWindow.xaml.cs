using System.ComponentModel;
using System.IO;
using System.Windows;

namespace PerDeviceMixer.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel = new();
    private readonly bool _startMinimized;
    private TrayIconService? _trayIcon;
    private bool _forceExit;
    private bool _disposed;

    public MainWindow(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        InitializeComponent();
        DataContext = _viewModel;
        _trayIcon = new TrayIconService(_viewModel);
        _trayIcon.ShowRequested += OnTrayShowRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await _viewModel.InitializeAsync();
        if (_startMinimized)
        {
            HideToTray();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (!_forceExit && _viewModel.ShouldMinimizeToTray)
        {
            eventArgs.Cancel = true;
            HideToTray();
            return;
        }

        try
        {
            if (!_viewModel.IsClosing) _viewModel.Close();
        }
        catch (Exception exception)
        {
            WriteShutdownError(exception);
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnTrayShowRequested(object? sender, EventArgs eventArgs) => ShowFromTray();

    private void OnTrayExitRequested(object? sender, EventArgs eventArgs)
    {
        _forceExit = true;
        Close();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal void ActivateFromExternal() => ShowFromTray();

    private static void WriteShutdownError(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PerDeviceMixer",
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "shutdown-errors.log"),
                $"[{DateTimeOffset.Now:O}] {exception}\n");
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_trayIcon is not null)
        {
            _trayIcon.ShowRequested -= OnTrayShowRequested;
            _trayIcon.ExitRequested -= OnTrayExitRequested;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
