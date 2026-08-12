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
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
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
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _shutdownSignal = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownSignalName);
        _watchTask = Task.Run(WatchForShowRequests);

        var startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        var window = new MainWindow(startMinimized)
        {
            ShowInTaskbar = !startMinimized,
            WindowState = startMinimized ? WindowState.Minimized : WindowState.Normal
        };
        MainWindow = window;
        window.Show();
    }

    private void WatchForShowRequests()
    {
        if (_showSignal is null || _shutdownSignal is null) return;
        var handles = new WaitHandle[] { _showSignal, _shutdownSignal };
        while (WaitHandle.WaitAny(handles) == 0)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window) window.ActivateFromExternal();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
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
