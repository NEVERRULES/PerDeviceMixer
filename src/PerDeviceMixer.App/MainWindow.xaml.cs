using System.ComponentModel;
using System.Windows;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly ApplicationController _controller;
    private readonly MixerEngine _engine;
    private readonly MainViewModel _viewModel;
    private bool _forceExit;
    private bool _disposed;

    internal MainWindow(
        ApplicationController controller,
        MixerEngine engine,
        UpdateCoordinator updates)
    {
        _controller = controller;
        _engine = engine;
        _viewModel = new MainViewModel(engine, controller, updates);
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs) =>
        await _viewModel.InitializeAsync();

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_forceExit || _controller.IsExiting)
        {
            _controller.BeginExitFromWindow();
            return;
        }

        if (_engine.Profiles.Settings.CloseBehavior != CloseBehavior.MinimizeToTray)
        {
            _controller.BeginExitFromWindow();
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Dispose();
        _controller.OnWindowClosed(this);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs) => Close();

    internal void ActivateWindow()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    internal void CloseForExit()
    {
        _forceExit = true;
        Close();
    }

    internal void ShowSettings()
    {
        _viewModel.NavigateToSettings();
        ActivateWindow();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        Closed -= OnClosed;
        DataContext = null;
        _viewModel.Dispose();
        Content = null;
        Resources.Clear();
        Icon = null;
        GC.SuppressFinalize(this);
    }
}
