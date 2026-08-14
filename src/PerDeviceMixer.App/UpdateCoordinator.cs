using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

internal sealed record UpdateState(
    bool IsChecking,
    bool IsDownloading,
    int DownloadProgress,
    string Message,
    UpdateReleaseInfo? AvailableRelease,
    bool LastCheckWasAutomatic);

internal sealed class UpdateStateChangedEventArgs(UpdateState state) : EventArgs
{
    public UpdateState State { get; } = state;
}

internal sealed class UpdateCoordinator : IDisposable
{
    private readonly MixerEngine _engine;
    private readonly GitHubUpdateService _service;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly SemaphoreSlim _settingsChanged = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _stateLock = new();
    private Task? _schedulerTask;
    private UpdateState _state = new(
        false,
        false,
        0,
        "尚未检查更新",
        null,
        false);
    private bool _disposed;

    public UpdateCoordinator(
        MixerEngine engine,
        GitHubUpdateService service,
        TimeProvider? timeProvider = null)
    {
        _engine = engine;
        _service = service;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<UpdateStateChangedEventArgs>? StateChanged;

    public UpdateState CurrentState
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_schedulerTask is not null) return;
        _service.CleanupOldUpdates(_timeProvider.GetUtcNow());
        _schedulerTask = Task.Run(RunSchedulerAsync);
    }

    public void NotifySettingsChanged()
    {
        if (_disposed) return;
        try
        {
            _settingsChanged.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public async Task<UpdateState> CheckAsync(
        bool manual,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        var operationToken = linkedCancellation.Token;
        if (!await _checkGate.WaitAsync(0, operationToken).ConfigureAwait(false))
        {
            return CurrentState;
        }

        try
        {
            SetState(CurrentState with
            {
                IsChecking = true,
                Message = manual ? "正在手动检查更新…" : "正在自动检查更新…",
                LastCheckWasAutomatic = !manual
            });

            var attempt = _timeProvider.GetUtcNow();
            _engine.UpdateSettings(settings => settings.LastUpdateAttemptUtc = attempt);
            try
            {
                var release = await _service.FindAvailableReleaseAsync(
                    ApplicationVersionInfo.Current,
                    operationToken).ConfigureAwait(false);
                var success = _timeProvider.GetUtcNow();
                _engine.UpdateSettings(settings => settings.LastSuccessfulUpdateCheckUtc = success);
                SetState(new UpdateState(
                    false,
                    false,
                    0,
                    release is null ? "当前已是最新版本" : $"发现新版本 {release.Version}",
                    release,
                    !manual));
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return CurrentState;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetState(CurrentState with
                {
                    IsChecking = false,
                    Message = "检查更新失败：" + exception.Message,
                    LastCheckWasAutomatic = !manual
                });
            }

            return CurrentState;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public async Task<string> DownloadInstallerAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        var release = CurrentState.AvailableRelease
            ?? throw new InvalidOperationException("没有可下载的新版本。");
        SetState(CurrentState with
        {
            IsDownloading = true,
            DownloadProgress = 0,
            Message = $"正在下载 {release.Version}…"
        });

        var progress = new Progress<int>(value => SetState(CurrentState with
        {
            IsDownloading = true,
            DownloadProgress = value,
            Message = $"正在下载 {release.Version} · {value}%"
        }));

        try
        {
            var path = await _service.DownloadInstallerAsync(
                release,
                progress,
                linkedCancellation.Token).ConfigureAwait(false);
            SetState(CurrentState with
            {
                IsDownloading = false,
                DownloadProgress = 100,
                Message = "安装包已下载并通过 SHA-256 校验"
            });
            return path;
        }
        catch
        {
            SetState(CurrentState with
            {
                IsDownloading = false,
                DownloadProgress = 0,
                Message = "更新下载失败"
            });
            throw;
        }
    }

    private async Task RunSchedulerAsync()
    {
        var cancellationToken = _shutdown.Token;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _timeProvider, cancellationToken)
                .ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                var settings = _engine.GetSettingsSnapshot();
                if (!settings.AutomaticUpdateChecks)
                {
                    await WaitForSettingsOrDelayAsync(TimeSpan.FromHours(1), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                var now = _timeProvider.GetUtcNow();
                var due = CalculateNextAutomaticCheck(settings);
                if (due > now)
                {
                    await WaitForSettingsOrDelayAsync(due - now, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await CheckAsync(manual: false, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task WaitForSettingsOrDelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, _timeProvider, cancellationToken);
        var settingsTask = _settingsChanged.WaitAsync(waitCancellation.Token);
        var completed = await Task.WhenAny(delayTask, settingsTask).ConfigureAwait(false);
        waitCancellation.Cancel();
        try
        {
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static int NormalizeInterval(int hours) => hours is 6 or 24 or 72 or 168 ? hours : 24;

    internal static DateTimeOffset CalculateNextAutomaticCheck(MixerSettings settings)
    {
        var interval = TimeSpan.FromHours(NormalizeInterval(settings.UpdateCheckIntervalHours));
        var nextSuccessDue = (settings.LastSuccessfulUpdateCheckUtc ?? DateTimeOffset.MinValue) + interval;
        var nextAttemptDue = (settings.LastUpdateAttemptUtc ?? DateTimeOffset.MinValue) + TimeSpan.FromHours(1);
        return nextSuccessDue > nextAttemptDue ? nextSuccessDue : nextAttemptDue;
    }

    private void SetState(UpdateState state)
    {
        lock (_stateLock) _state = state;
        StateChanged?.Invoke(this, new UpdateStateChangedEventArgs(state));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        try
        {
            _schedulerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _shutdown.Dispose();
        _service.Dispose();
    }
}
