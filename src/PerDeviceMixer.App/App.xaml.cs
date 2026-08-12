using System.Threading;
using System.Windows;

namespace PerDeviceMixer.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string InstanceMutexName = @"Local\PerDeviceMixer.Singleton";
    private const string ShowSignalName = @"Local\PerDeviceMixer.Show";
    private const string ShutdownSignalName = @"Local\PerDeviceMixer.Shutdown";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private EventWaitHandle? _shutdownSignal;
    private Task? _watchTask;
    private ApplicationController? _controller;
    private bool _ownsInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        _ownsInstance = createdNew;
        if (!createdNew)
        {
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
            {
                using (signal) signal.Set();
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _shutdownSignal = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownSignalName);
        _watchTask = Task.Run(WatchForShowRequests);

        var startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        try
        {
            _controller = new ApplicationController(this);
            await _controller.InitializeAsync();
            if (!startMinimized) _controller.ShowWindow();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "PerDeviceMixer 启动失败：" + exception.Message,
                "PerDeviceMixer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void WatchForShowRequests()
    {
        if (_showSignal is null || _shutdownSignal is null) return;
        var handles = new WaitHandle[] { _showSignal, _shutdownSignal };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                _controller?.ShowWindow();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _controller = null;
        Dispose();
        base.OnExit(e);
    }

    internal bool ReleaseInstanceMutexForUpdate()
    {
        if (!_ownsInstance || _instanceMutex is null) return false;
        _instanceMutex.ReleaseMutex();
        _instanceMutex.Dispose();
        _instanceMutex = null;
        _ownsInstance = false;
        return true;
    }

    internal bool TryReacquireInstanceMutex()
    {
        if (_ownsInstance) return true;
        var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _instanceMutex = mutex;
        _ownsInstance = true;
        return true;
    }

    public void Dispose()
    {
        _shutdownSignal?.Set();
        try
        {
            _watchTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _watchTask = null;
        _showSignal?.Dispose();
        _showSignal = null;
        _shutdownSignal?.Dispose();
        _shutdownSignal = null;
        if (_ownsInstance)
        {
            _instanceMutex?.ReleaseMutex();
            _ownsInstance = false;
        }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        GC.SuppressFinalize(this);
    }
}
