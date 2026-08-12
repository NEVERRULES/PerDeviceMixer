using System.Windows;
using System.Windows.Threading;
using PerDeviceMixer.Audio;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

internal sealed class ApplicationController : IDisposable
{
    private readonly App _application;
    private readonly MixerEngine _engine = new(new CoreAudioService(), new JsonProfileStore());
    private readonly UpdateCoordinator _updates;
    private TrayIconService? _trayIcon;
    private MainWindow? _window;
    private ResourceDictionary? _windowResources;
    private readonly Dictionary<AppPage, ResourceDictionary> _pageResources = [];
    private CancellationTokenSource? _memoryOptimizationCancellation;
    private string? _notifiedUpdateVersion;
    private bool _disposed;

    public ApplicationController(App application)
    {
        _application = application;
        _updates = new UpdateCoordinator(_engine, new GitHubUpdateService());
    }

    public bool IsExiting { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _engine.InitializeAsync(cancellationToken);
        _trayIcon = new TrayIconService(_engine);
        _trayIcon.ShowRequested += OnShowRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.ProjectRequested += OnProjectRequested;
        _trayIcon.FeedbackRequested += OnFeedbackRequested;
        _trayIcon.CheckUpdatesRequested += OnCheckUpdatesRequested;
        _trayIcon.MenuClosed += OnTrayMenuClosed;
        _updates.StateChanged += OnUpdateStateChanged;
        _updates.Start();
    }

    public void ShowWindow()
    {
        if (_disposed || IsExiting) return;
        CancelBackgroundMemoryOptimization();
        if (_window is not null)
        {
            _window.ActivateWindow();
            return;
        }

        EnsureWindowResources();
        _window = new MainWindow(this, _engine, _updates);
        _application.MainWindow = _window;
        _window.Show();
    }

    public void EnterTrayMode()
    {
        if (_disposed || IsExiting || _window is not null) return;
        ScheduleBackgroundMemoryOptimization();
    }

    public void BeginExitFromWindow() => IsExiting = true;

    private void EnsureWindowResources()
    {
        if (_windowResources is not null) return;
        _windowResources = new ResourceDictionary
        {
            Source = new Uri(
                "/PerDeviceMixer.App;component/MainWindowResources.xaml",
                UriKind.Relative)
        };
        _application.Resources.MergedDictionaries.Add(_windowResources);
    }

    private void ReleaseWindowResources()
    {
        foreach (var resources in _pageResources.Values)
        {
            _application.Resources.MergedDictionaries.Remove(resources);
            resources.Clear();
        }
        _pageResources.Clear();
        if (_windowResources is null) return;
        _application.Resources.MergedDictionaries.Remove(_windowResources);
        _windowResources.Clear();
        _windowResources = null;
    }

    internal void EnsurePageResources(AppPage page)
    {
        if (page == AppPage.Mixer || _pageResources.ContainsKey(page)) return;
        var resourceName = page switch
        {
            AppPage.Devices => "DevicesPageResources.xaml",
            AppPage.Settings => "SettingsPageResources.xaml",
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
        var resources = new ResourceDictionary
        {
            Source = new Uri($"/PerDeviceMixer.App;component/{resourceName}", UriKind.Relative)
        };
        _application.Resources.MergedDictionaries.Add(resources);
        _pageResources.Add(page, resources);
    }

    public void RequestExit()
    {
        if (_disposed || IsExiting) return;
        CancelBackgroundMemoryOptimization();
        IsExiting = true;
        if (_window is not null)
        {
            _window.CloseForExit();
        }
        else
        {
            _application.Shutdown();
        }
    }

    public void OnWindowClosed(MainWindow window)
    {
        if (!ReferenceEquals(_window, window)) return;
        if (ReferenceEquals(_application.MainWindow, window)) _application.MainWindow = null;
        _window = null;
        ReleaseWindowResources();
        if (IsExiting)
        {
            _application.Shutdown();
        }
        else
        {
            ScheduleBackgroundMemoryOptimization();
        }
    }

    private void OnShowRequested(object? sender, EventArgs eventArgs) => ShowWindow();

    private void OnExitRequested(object? sender, EventArgs eventArgs) => RequestExit();

    private static void OnProjectRequested(object? sender, EventArgs eventArgs) => OpenProject();

    private void OnFeedbackRequested(object? sender, EventArgs eventArgs) => ShowFeedback();

    private void OnTrayMenuClosed(object? sender, EventArgs eventArgs) =>
        ScheduleBackgroundMemoryOptimization();

    private async void OnCheckUpdatesRequested(object? sender, EventArgs eventArgs)
    {
        ShowWindow();
        _window?.ShowSettings();
        await _updates.CheckAsync(manual: true);
    }

    private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs eventArgs)
    {
        _ = _application.Dispatcher.BeginInvoke(() =>
        {
            var release = eventArgs.State.AvailableRelease;
            _trayIcon?.SetUpdateAvailable(release?.Version.ToString());
            if (release is not null &&
                eventArgs.State.LastCheckWasAutomatic &&
                !string.Equals(_notifiedUpdateVersion, release.Version.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _notifiedUpdateVersion = release.Version.ToString();
                _trayIcon?.ShowUpdateNotification(_notifiedUpdateVersion);
            }

            if (_window is null &&
                !eventArgs.State.IsChecking &&
                !eventArgs.State.IsDownloading)
            {
                ScheduleBackgroundMemoryOptimization();
            }
        });
    }

    private void ScheduleBackgroundMemoryOptimization()
    {
        if (_disposed || IsExiting || HasVisibleWindow()) return;
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _memoryOptimizationCancellation, cancellation);
        if (previous is not null)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _ = OptimizeBackgroundMemoryAsync(cancellation);
    }

    private async Task OptimizeBackgroundMemoryAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token).ConfigureAwait(false);
            await _application.Dispatcher.InvokeAsync(
                () =>
                {
                    if (!cancellation.IsCancellationRequested &&
                        !_disposed &&
                        !IsExiting &&
                        !HasVisibleWindow())
                    {
                        BackgroundMemoryOptimizer.TrimCurrentProcess();
                    }
                },
                DispatcherPriority.ApplicationIdle,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _ = Interlocked.CompareExchange(
                ref _memoryOptimizationCancellation,
                null,
                cancellation);
            cancellation.Dispose();
        }
    }

    private void CancelBackgroundMemoryOptimization()
    {
        var cancellation = Interlocked.Exchange(ref _memoryOptimizationCancellation, null);
        if (cancellation is null) return;
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool HasVisibleWindow() =>
        _window is not null || _application.Windows.Cast<Window>().Any(window => window.IsVisible);

    public static void OpenProject() => OpenSupportLink(ProjectLinks.Project);

    public static void OpenReleases() => OpenSupportLink(ProjectLinks.Releases);

    public void OpenAvailableRelease()
    {
        var release = _updates.CurrentState.AvailableRelease;
        OpenSupportLink(release?.ReleaseUrl.AbsoluteUri ?? ProjectLinks.Releases);
    }

    private static void OpenSupportLink(string url)
    {
        try
        {
            ExternalLinkService.Open(url);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "无法打开 GitHub：" + exception.Message,
                "PerDeviceMixer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public void ShowFeedback()
    {
        var basic = FeedbackInformation.CreateBasic();
        var diagnostics = CreateAudioDiagnostics();
        var dialog = new FeedbackDialog(basic, diagnostics)
        {
            Owner = _window
        };
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            ScheduleBackgroundMemoryOptimization();
        }
    }

    public async Task DownloadAndInstallAvailableUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (InstallationDetector.GetInstallType() != ApplicationInstallType.Installed)
        {
            OpenAvailableRelease();
            return;
        }

        var installerPath = await _updates.DownloadInstallerAsync(cancellationToken);
        _engine.Flush();
        InstallerHandoff.Launch(
            installerPath,
            _application.ReleaseInstanceMutexForUpdate,
            _application.TryReacquireInstanceMutex);

        IsExiting = true;
        if (_window is not null) _window.CloseForExit();
        else _application.Shutdown();
    }

    private string CreateAudioDiagnostics()
    {
        try
        {
            var outputs = _engine.GetDevices();
            var inputs = _engine.GetCaptureDevices();
            var outputNames = string.Join("、", outputs.Select(item => item.Name));
            var inputNames = string.Join("、", inputs.Select(item => item.Name));
            return $"""

                   ## 可选音频诊断

                   - 输出设备数量：{outputs.Count}
                   - 输出设备名称：{(string.IsNullOrWhiteSpace(outputNames) ? "无" : outputNames)}
                   - 输入设备数量：{inputs.Count}
                   - 输入设备名称：{(string.IsNullOrWhiteSpace(inputNames) ? "无" : inputNames)}

                   <!-- 未包含设备 ID、应用列表、音量配置、用户名或日志。 -->
                   """;
        }
        catch (Exception exception)
        {
            return $"\n\n## 可选音频诊断\n\n- 读取失败：{exception.GetType().Name}\n";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelBackgroundMemoryOptimization();
        if (_trayIcon is not null)
        {
            _trayIcon.ShowRequested -= OnShowRequested;
            _trayIcon.ExitRequested -= OnExitRequested;
            _trayIcon.ProjectRequested -= OnProjectRequested;
            _trayIcon.FeedbackRequested -= OnFeedbackRequested;
            _trayIcon.CheckUpdatesRequested -= OnCheckUpdatesRequested;
            _trayIcon.MenuClosed -= OnTrayMenuClosed;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _updates.StateChanged -= OnUpdateStateChanged;
        _updates.Dispose();
        ReleaseWindowResources();

        try
        {
            _engine.Flush();
        }
        finally
        {
            _engine.Dispose();
        }
    }
}
