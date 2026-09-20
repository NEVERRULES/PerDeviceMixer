using System.Diagnostics;
using System.Threading.Channels;
using System.Security.Cryptography;
using System.Text;

namespace PerDeviceMixer.Core;

public sealed class MixerEngine : IDisposable
{
    private static readonly TimeSpan RenderDevicePriorityInitialRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan RenderDevicePriorityRetryInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RenderDevicePriorityRetryWindow = TimeSpan.FromSeconds(12);

    private readonly IAudioService audio;
    private readonly IProfileStore profileStore;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly IAudioDiagnosticSink? _diagnostics;
    private readonly object _pendingVolumeGate = new();
    private sealed class PendingAudioEvent(AudioStateChangedEventArgs args)
    {
        public AudioStateChangedEventArgs Args { get; set; } = args;
    }

    private PendingAudioEvent? _lastPendingAudioEvent;
    private int _pendingVolumeCount;
    private readonly Channel<PendingAudioEvent> _audioEvents =
        Channel.CreateBounded<PendingAudioEvent>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly CancellationTokenSource _audioEventWorkerShutdown = new();
    private readonly Task _audioEventWorker;
    private readonly Channel<bool> _saveRequests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly CancellationTokenSource _saveWorkerShutdown = new();
    private readonly Task _saveWorker;
    private readonly object _renderDevicePriorityGate = new();
    private readonly HashSet<string> _activeRenderDeviceIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _renderDevicePriority = [];
    private readonly object _renderDeviceReconcileGate = new();
    private readonly CancellationTokenSource _renderDeviceReconcileShutdown = new();
    private readonly SemaphoreSlim _renderDeviceReconcileWakeSignal = new(0, 1);
    private MixerProfileDocument _document = new();
    private Task? _renderDeviceReconcileTask;
    private string? _pendingRenderDeviceChangeId;
    private long _saveRevision;
    private long _renderDeviceReconcileRevision;
    private bool _renderDevicePriorityInitialized;
    private bool _isRestoring;
    private bool _disposed;

    public MixerEngine(
        IAudioService audio,
        IProfileStore profileStore,
        IAudioDiagnosticSink? diagnostics = null)
    {
        this.audio = audio;
        this.profileStore = profileStore;
        _diagnostics = diagnostics;
        _audioEventWorker = Task.Run(RunAudioEventWorkerAsync);
        _saveWorker = Task.Run(RunSaveWorkerAsync);
    }

    public event EventHandler<AudioStateChangedEventArgs>? MixerChanged;
    public event EventHandler<ProfileSaveStateChangedEventArgs>? ProfileSaveStateChanged;

    public MixerProfileDocument Profiles => _document;
    public string ProfilePath => profileStore.FilePath;

    public MixerSettings GetSettingsSnapshot()
    {
        _operationLock.Wait();
        try
        {
            return new MixerSettings
            {
                AutoLearn = _document.Settings.AutoLearn,
                AutoRestore = _document.Settings.AutoRestore,
                RestoreNewSessions = _document.Settings.RestoreNewSessions,
                SaveMuteState = _document.Settings.SaveMuteState,
                ShowDeviceSwitchToast = _document.Settings.ShowDeviceSwitchToast,
                ShowSupportedHeadphoneBattery = _document.Settings.ShowSupportedHeadphoneBattery,
                AudioDiagnosticsEnabled = _document.Settings.AudioDiagnosticsEnabled,
                SaveDebounceMilliseconds = _document.Settings.SaveDebounceMilliseconds,
                StartWithWindows = _document.Settings.StartWithWindows,
                CloseBehavior = _document.Settings.CloseBehavior,
                AutomaticUpdateChecks = _document.Settings.AutomaticUpdateChecks,
                UpdateCheckIntervalHours = _document.Settings.UpdateCheckIntervalHours,
                LastSuccessfulUpdateCheckUtc = _document.Settings.LastSuccessfulUpdateCheckUtc,
                LastUpdateAttemptUtc = _document.Settings.LastUpdateAttemptUtc
            };
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _document = await profileStore.LoadAsync(cancellationToken);
        audio.StateChanged += OnAudioStateChanged;
        audio.StartMonitoring();
        try
        {
            InitializeRenderDevicePriority();
        }
        catch (Exception exception)
        {
            // Endpoint enumeration can fail while Bluetooth, HDMI, or sleep-resume is settling.
            // The next device notification will initialize the priority state again.
            RecordDiagnostic(
                new AudioStateChangedEventArgs(AudioChangeKind.DeviceCollection),
                "priority-init-failed",
                exception);
        }

        var snapshot = await GetSnapshotWithRetryAsync(5, cancellationToken);
        if (snapshot is null)
        {
            await profileStore.SaveAsync(_document, cancellationToken);
            MarkSaved();
            return;
        }

        if (_document.Settings.AutoRestore && _document.Devices.ContainsKey(snapshot.Endpoint.Id))
        {
            Restore(snapshot.Endpoint.Id);
            var restoredSnapshot = audio.GetDefaultMixerSnapshot();
            if (restoredSnapshot is not null)
            {
                await CaptureAndSaveAsync(restoredSnapshot, cancellationToken);
            }
        }
        else
        {
            await CaptureAndSaveAsync(snapshot, cancellationToken);
        }

        MixerChanged?.Invoke(
            this,
            new AudioStateChangedEventArgs(
                AudioChangeKind.DefaultDevice,
                snapshot.Endpoint.Id,
                volume: snapshot.Endpoint.MasterVolume,
                isMuted: snapshot.Endpoint.IsMuted));
    }

    public IReadOnlyList<AudioEndpointInfo> GetDevices() => audio.GetRenderDevices();

    public IReadOnlyList<AudioEndpointInfo> GetCaptureDevices() => audio.GetCaptureDevices();

    public MixerSnapshot? GetCurrentSnapshot() => audio.GetDefaultMixerSnapshot();

    public string? GetDefaultCaptureDeviceId() => audio.GetDefaultCaptureDeviceId();

    public void SetDefaultOutputDevice(string deviceId)
    {
        audio.SetDefaultDevice(deviceId, AudioDeviceDirection.Output);
        PromoteRenderDevice(deviceId);
    }

    public void SetDefaultInputDevice(string deviceId) =>
        audio.SetDefaultDevice(deviceId, AudioDeviceDirection.Input);

    public void SetInputVolume(float volume, bool? muted = null)
    {
        var id = audio.GetDefaultCaptureDeviceId();
        if (id is null) return;
        audio.SetMasterVolume(id, Clamp(volume), muted);
    }

    public void SetMasterVolume(float volume, bool? muted = null)
    {
        var id = audio.GetDefaultRenderDeviceId();
        if (id is null) return;
        var clamped = Clamp(volume);
        audio.SetMasterVolume(id, clamped, muted);
        UpdateProfileFromLocalChange(id, null, clamped, muted);
        MixerChanged?.Invoke(
            this,
            new AudioStateChangedEventArgs(
                AudioChangeKind.MasterVolume,
                id,
                volume: clamped,
                isMuted: muted));
    }

    public void SetApplicationVolume(string applicationKey, float volume, bool? muted = null)
    {
        var id = audio.GetDefaultRenderDeviceId();
        if (id is null) return;
        var clamped = Clamp(volume);
        audio.SetApplicationVolume(id, applicationKey, clamped, muted);
        UpdateProfileFromLocalChange(id, applicationKey, clamped, muted);
    }

    private void UpdateProfileFromLocalChange(
        string deviceId,
        string? applicationKey,
        float volume,
        bool? muted)
    {
        if (!_document.Settings.AutoLearn) return;

        _operationLock.Wait();
        try
        {
            if (!_document.Devices.TryGetValue(deviceId, out var profile)) return;

            var now = DateTimeOffset.UtcNow;
            if (applicationKey is null)
            {
                profile.MasterVolume = volume;
                if (muted.HasValue) profile.MasterMuted = muted.Value;
            }
            else if (profile.Applications.TryGetValue(applicationKey, out var application))
            {
                application.Volume = volume;
                if (muted.HasValue) application.Muted = muted.Value;
                application.LastUpdatedUtc = now;
            }

            profile.LastUpdatedUtc = now;
        }
        finally
        {
            _operationLock.Release();
        }

        ScheduleSave();
    }

    public void UpdateSettings(Action<MixerSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _operationLock.Wait();
        try
        {
            update(_document.Settings);
            _document.Settings.SaveDebounceMilliseconds = Math.Clamp(
                _document.Settings.SaveDebounceMilliseconds,
                100,
                5000);
            if (_document.Settings.UpdateCheckIntervalHours is not (6 or 24 or 72 or 168))
            {
                _document.Settings.UpdateCheckIntervalHours = 24;
            }
        }
        finally
        {
            _operationLock.Release();
        }

        ScheduleSave();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotWithRetryAsync(3, cancellationToken);
        if (snapshot is not null)
        {
            await CaptureAndSaveAsync(snapshot, cancellationToken);
        }
        else
        {
            await SaveDocumentAsync(cancellationToken);
            MarkSaved();
        }
    }

    public void Flush()
    {
        var snapshot = audio.GetDefaultMixerSnapshot();
        if (snapshot is not null)
        {
            Capture(snapshot);
        }
        else
        {
            _operationLock.Wait();
            try
            {
                profileStore.Save(_document);
                MarkSaved();
            }
            finally
            {
                _operationLock.Release();
            }
        }
    }

    private void Capture(MixerSnapshot snapshot)
    {
        _operationLock.Wait();
        try
        {
            UpdateDocument(snapshot);
            profileStore.Save(_document);
            MarkSaved();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private static async Task DelaySnapshotRetryAsync(CancellationToken cancellationToken) =>
        await Task.Delay(100, cancellationToken);

    private async Task<MixerSnapshot?> GetSnapshotWithRetryAsync(
        int attempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var snapshot = audio.GetDefaultMixerSnapshot();
            if (snapshot is not null) return snapshot;
            if (attempt + 1 < attempts)
            {
                await DelaySnapshotRetryAsync(cancellationToken);
            }
        }

        return null;
    }

    private void OnAudioStateChanged(object? sender, AudioStateChangedEventArgs args)
    {
        if (_disposed) return;
        lock (_pendingVolumeGate)
        {
            var isVolume = IsVolumeNotification(args);
            var previous = _lastPendingAudioEvent?.Args;
            if (isVolume && previous is not null && previous.Kind == args.Kind &&
                previous.DeviceId == args.DeviceId && previous.ApplicationKey == args.ApplicationKey)
            {
                _lastPendingAudioEvent!.Args = args;
                return;
            }

            // Reserve capacity for lifecycle events. Coalesce only adjacent volume events
            // so updates never cross a device switch or session creation boundary.
            var pending = new PendingAudioEvent(args);
            if ((!isVolume || _pendingVolumeCount < 128) && _audioEvents.Writer.TryWrite(pending))
            {
                if (isVolume) _pendingVolumeCount++;
                _lastPendingAudioEvent = pending;
                return;
            }
        }

        RecordDiagnostic(args, "queue-full");
    }

    private static bool IsVolumeNotification(AudioStateChangedEventArgs args) =>
        args.Kind is AudioChangeKind.MasterVolume or AudioChangeKind.SessionVolume or AudioChangeKind.CaptureVolume;

    private async Task RunAudioEventWorkerAsync()
    {
        var reader = _audioEvents.Reader;
        var cancellationToken = _audioEventWorkerShutdown.Token;
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var pending))
                {
                    AudioStateChangedEventArgs args;
                    lock (_pendingVolumeGate)
                    {
                        args = pending.Args;
                        if (IsVolumeNotification(args)) _pendingVolumeCount--;
                        if (ReferenceEquals(_lastPendingAudioEvent, pending)) _lastPendingAudioEvent = null;
                    }

                    await HandleAudioStateChangedAsync(args).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordDiagnostic(
                new AudioStateChangedEventArgs(AudioChangeKind.DeviceCollection),
                "worker-failed",
                exception);
        }
    }

    private async Task HandleAudioStateChangedAsync(AudioStateChangedEventArgs args)
    {
        if (_disposed) return;

        try
        {
            RecordDiagnostic(args, "received");
            if (args.Kind == AudioChangeKind.DeviceCollection)
            {
                if (args.DeviceDirection == AudioDeviceDirection.Input)
                {
                    MixerChanged?.Invoke(this, args);
                    RecordDiagnostic(args, "ignored-input");
                    return;
                }

                ScheduleRenderDevicePriorityReconcile(args.DeviceId);
                MixerChanged?.Invoke(this, args);
                RecordDiagnostic(args, "scheduled");
                return;
            }

            if (args.Kind == AudioChangeKind.DefaultDevice)
            {
                // A removal can make Windows publish its own fallback before the collection event.
                // Reconcile membership here as well, but never interpret an arbitrary Windows
                // default change as a newly connected device.
                if (ReconcileRenderDevicePriority())
                {
                    RecordDiagnostic(args, "redirected");
                    return;
                }

                var snapshot = audio.GetDefaultMixerSnapshot();
                if (snapshot is null) return;

                if (_document.Settings.AutoRestore && _document.Devices.ContainsKey(snapshot.Endpoint.Id))
                {
                    Restore(snapshot.Endpoint.Id);
                    var restoredSnapshot = audio.GetDefaultMixerSnapshot();
                    if (restoredSnapshot is not null)
                    {
                        await CaptureAndSaveAsync(restoredSnapshot);
                    }
                }
                else if (_document.Settings.AutoLearn)
                {
                    await CaptureAndSaveAsync(snapshot);
                }

                MixerChanged?.Invoke(this, args);
                RecordDiagnostic(args, "completed");
                return;
            }

            if (args.Kind == AudioChangeKind.SessionCreated)
            {
                if (_document.Settings.AutoRestore &&
                    _document.Settings.RestoreNewSessions &&
                    args.ApplicationKey is not null)
                {
                    RestoreApplication(args.ApplicationKey);
                }

                if (_document.Settings.AutoLearn)
                {
                    var snapshot = audio.GetDefaultMixerSnapshot();
                    if (snapshot is not null) await CaptureAndSaveAsync(snapshot);
                }

                MixerChanged?.Invoke(this, args);
                RecordDiagnostic(args, "completed");
                return;
            }

            if (!_isRestoring && _document.Settings.AutoLearn &&
                args.Volume.HasValue &&
                args.IsMuted.HasValue &&
                args.DeviceId is not null &&
                args.Kind is AudioChangeKind.MasterVolume or AudioChangeKind.SessionVolume)
            {
                await UpdateProfileFromNotificationAsync(args);
                ScheduleSave();
            }

            MixerChanged?.Invoke(this, args);
            RecordDiagnostic(args, "completed");
        }
        catch (Exception exception)
        {
            // A transient endpoint disappearing during a Bluetooth/HDMI switch is expected.
            // Record the failure when explicitly enabled; the next notification can refresh the state.
            RecordDiagnostic(args, "failed", exception);
        }
    }

    private void ScheduleRenderDevicePriorityReconcile(string? changedDeviceId)
    {
        if (_disposed) return;

        lock (_renderDeviceReconcileGate)
        {
            if (!string.IsNullOrWhiteSpace(changedDeviceId))
            {
                _pendingRenderDeviceChangeId = changedDeviceId;
            }

            _renderDeviceReconcileRevision++;
            if (_renderDeviceReconcileTask is null || _renderDeviceReconcileTask.IsCompleted)
            {
                _renderDeviceReconcileTask = Task.Run(RunRenderDevicePriorityReconcileAsync);
            }
            else if (_renderDeviceReconcileWakeSignal.CurrentCount == 0)
            {
                _renderDeviceReconcileWakeSignal.Release();
            }
        }
    }

    private async Task RunRenderDevicePriorityReconcileAsync()
    {
        var cancellationToken = _renderDeviceReconcileShutdown.Token;
        var retryWindowStartedAt = Stopwatch.GetTimestamp();
        var nextDelay = RenderDevicePriorityInitialRetryDelay;
        string? changedDeviceId = null;

        try
        {
            while (!_disposed)
            {
                lock (_renderDeviceReconcileGate)
                {
                    if (_pendingRenderDeviceChangeId is not null)
                    {
                        changedDeviceId = _pendingRenderDeviceChangeId;
                        _pendingRenderDeviceChangeId = null;
                    }
                }

                await WaitForRenderDeviceReconcileAsync(nextDelay, cancellationToken).ConfigureAwait(false);
                if (_disposed) break;

                long observedRevision;
                lock (_renderDeviceReconcileGate)
                {
                    observedRevision = _renderDeviceReconcileRevision;
                    if (_pendingRenderDeviceChangeId is not null)
                    {
                        changedDeviceId = _pendingRenderDeviceChangeId;
                        _pendingRenderDeviceChangeId = null;
                    }
                }

                var diagnosticArgs = new AudioStateChangedEventArgs(
                    AudioChangeKind.DeviceCollection,
                    changedDeviceId);
                var redirected = false;
                try
                {
                    redirected = ReconcileRenderDevicePriority(changedDeviceId);
                    RecordDiagnostic(
                        diagnosticArgs,
                        redirected ? "priority-redirected" : "priority-checked");
                }
                catch (Exception exception)
                {
                    RecordDiagnostic(diagnosticArgs, "priority-failed", exception);
                }

                lock (_renderDeviceReconcileGate)
                {
                    var hasNewWork = observedRevision != _renderDeviceReconcileRevision ||
                        _pendingRenderDeviceChangeId is not null;
                    if (!hasNewWork &&
                        (redirected || Stopwatch.GetElapsedTime(retryWindowStartedAt) >= RenderDevicePriorityRetryWindow))
                    {
                        _renderDeviceReconcileTask = null;
                        return;
                    }

                    if (hasNewWork)
                    {
                        retryWindowStartedAt = Stopwatch.GetTimestamp();
                        nextDelay = TimeSpan.Zero;
                    }
                    else
                    {
                        nextDelay = RenderDevicePriorityRetryInterval;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        lock (_renderDeviceReconcileGate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                _renderDeviceReconcileTask = null;
            }
        }
    }

    private async Task WaitForRenderDeviceReconcileAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay == TimeSpan.Zero) return;

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, waitCancellation.Token);
        var wakeTask = _renderDeviceReconcileWakeSignal.WaitAsync(waitCancellation.Token);
        var completedTask = await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);

        if (completedTask == wakeTask)
        {
            await wakeTask.ConfigureAwait(false);
            waitCancellation.Cancel();
            try
            {
                await delayTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        await delayTask.ConfigureAwait(false);
        waitCancellation.Cancel();
        try
        {
            await wakeTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void InitializeRenderDevicePriority()
    {
        var deviceIds = audio.GetActiveRenderDeviceIds();
        var defaultDeviceId = audio.GetDefaultRenderDeviceId();
        lock (_renderDevicePriorityGate)
        {
            InitializeRenderDevicePriorityCore(
                deviceIds,
                defaultDeviceId);
        }
    }

    private bool ReconcileRenderDevicePriority(string? changedDeviceId = null)
    {
        var activeIds = audio.GetActiveRenderDeviceIds()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var defaultDeviceId = audio.GetDefaultRenderDeviceId();
        string? preferredDeviceId;
        bool priorityChanged;

        lock (_renderDevicePriorityGate)
        {
            if (!_renderDevicePriorityInitialized)
            {
                InitializeRenderDevicePriorityCore(activeIds, defaultDeviceId);
                if (changedDeviceId is not null && _activeRenderDeviceIds.Contains(changedDeviceId))
                {
                    MoveRenderDeviceToTopCore(changedDeviceId);
                    priorityChanged = true;
                }
                else
                {
                    priorityChanged = false;
                }
            }
            else
            {
                var nextActiveIds = activeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var addedIds = activeIds
                    .Where(id => !_activeRenderDeviceIds.Contains(id))
                    .ToArray();
                priorityChanged = !_activeRenderDeviceIds.SetEquals(nextActiveIds);

                // Preserve the device that was in use immediately before a new endpoint arrived.
                // This also respects a default chosen directly in Windows, not only in this app.
                if (addedIds.Length > 0 &&
                    defaultDeviceId is not null &&
                    nextActiveIds.Contains(defaultDeviceId) &&
                    !addedIds.Contains(defaultDeviceId, StringComparer.OrdinalIgnoreCase))
                {
                    MoveRenderDeviceToTopCore(defaultDeviceId);
                }

                _renderDevicePriority.RemoveAll(id => !nextActiveIds.Contains(id));

                foreach (var id in activeIds)
                {
                    if (_activeRenderDeviceIds.Contains(id) ||
                        string.Equals(id, changedDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _renderDevicePriority.Add(id);
                }

                if (changedDeviceId is not null &&
                    addedIds.Contains(changedDeviceId, StringComparer.OrdinalIgnoreCase))
                {
                    priorityChanged |= !string.Equals(
                        _renderDevicePriority.LastOrDefault(),
                        changedDeviceId,
                        StringComparison.OrdinalIgnoreCase);
                    MoveRenderDeviceToTopCore(changedDeviceId);
                }

                _activeRenderDeviceIds.Clear();
                _activeRenderDeviceIds.UnionWith(nextActiveIds);
            }

            preferredDeviceId = _renderDevicePriority.LastOrDefault(
                id => _activeRenderDeviceIds.Contains(id));
        }

        if (!priorityChanged || preferredDeviceId is null ||
            string.Equals(preferredDeviceId, defaultDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            audio.SetDefaultDevice(preferredDeviceId, AudioDeviceDirection.Output);
        }
        catch
        {
            lock (_renderDevicePriorityGate)
            {
                _activeRenderDeviceIds.Remove(preferredDeviceId);
                _renderDevicePriority.RemoveAll(
                    id => string.Equals(id, preferredDeviceId, StringComparison.OrdinalIgnoreCase));
            }

            throw;
        }

        return true;
    }

    private void InitializeRenderDevicePriorityCore(
        IEnumerable<string> activeDeviceIds,
        string? defaultDeviceId)
    {
        _activeRenderDeviceIds.Clear();
        _renderDevicePriority.Clear();

        foreach (var id in activeDeviceIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _activeRenderDeviceIds.Add(id);
            if (!string.Equals(id, defaultDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                _renderDevicePriority.Add(id);
            }
        }

        if (defaultDeviceId is not null && _activeRenderDeviceIds.Contains(defaultDeviceId))
        {
            _renderDevicePriority.Add(defaultDeviceId);
        }

        _renderDevicePriorityInitialized = true;
    }

    private void PromoteRenderDevice(string deviceId)
    {
        lock (_renderDevicePriorityGate)
        {
            _activeRenderDeviceIds.Add(deviceId);
            MoveRenderDeviceToTopCore(deviceId);
            _renderDevicePriorityInitialized = true;
        }
    }

    private void MoveRenderDeviceToTopCore(string deviceId)
    {
        _renderDevicePriority.RemoveAll(
            id => string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase));
        _renderDevicePriority.Add(deviceId);
    }

    private void RecordDiagnostic(
        AudioStateChangedEventArgs args,
        string stage,
        Exception? exception = null)
    {
        if (_diagnostics is null || !_document.Settings.AudioDiagnosticsEnabled) return;
        _diagnostics.Write(new AudioDiagnosticEntry(
            DateTimeOffset.UtcNow,
            args.Kind,
            stage,
            ToDiagnosticToken(args.DeviceId),
            ToDiagnosticToken(args.ApplicationKey),
            exception?.GetType().Name));
    }

    private static string? ToDiagnosticToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash)[..12];
    }

    private async Task UpdateProfileFromNotificationAsync(AudioStateChangedEventArgs args)
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_document.Devices.TryGetValue(args.DeviceId!, out var profile)) return;

            var now = DateTimeOffset.UtcNow;
            if (args.Kind == AudioChangeKind.MasterVolume)
            {
                profile.MasterVolume = Clamp(args.Volume!.Value);
                profile.MasterMuted = args.IsMuted!.Value;
                profile.LastUpdatedUtc = now;
                return;
            }

            if (args.ApplicationKey is not null &&
                profile.Applications.TryGetValue(args.ApplicationKey, out var app))
            {
                app.Volume = Clamp(args.Volume!.Value);
                app.Muted = args.IsMuted!.Value;
                app.LastUpdatedUtc = now;
                profile.LastUpdatedUtc = now;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void ScheduleSave()
    {
        if (_disposed) return;
        Interlocked.Increment(ref _saveRevision);
        RaiseSaveState(ProfileSaveState.Pending);
        _saveRequests.Writer.TryWrite(true);
    }

    private async Task RunSaveWorkerAsync()
    {
        var reader = _saveRequests.Reader;
        var cancellationToken = _saveWorkerShutdown.Token;

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out _))
                {
                }

                bool changedDuringDelay;
                do
                {
                    var delay = Math.Clamp(_document.Settings.SaveDebounceMilliseconds, 100, 5000);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    changedDuringDelay = false;
                    while (reader.TryRead(out _)) changedDuringDelay = true;
                }
                while (changedDuringDelay);

                var revision = Volatile.Read(ref _saveRevision);
                try
                {
                    await SaveDocumentAsync(cancellationToken).ConfigureAwait(false);
                    if (Volatile.Read(ref _saveRevision) == revision)
                    {
                        RaiseSaveState(ProfileSaveState.Saved);
                    }
                    else
                    {
                        _saveRequests.Writer.TryWrite(true);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    RaiseSaveState(ProfileSaveState.Failed, exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task SaveDocumentAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await profileStore.SaveAsync(_document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task CaptureAndSaveAsync(
        MixerSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UpdateDocument(snapshot);
            await profileStore.SaveAsync(_document, cancellationToken).ConfigureAwait(false);
            MarkSaved();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void UpdateDocument(MixerSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_document.Devices.TryGetValue(snapshot.Endpoint.Id, out var profile))
        {
            profile = new DeviceProfile
            {
                DeviceId = snapshot.Endpoint.Id,
                Name = snapshot.Endpoint.Name
            };
            _document.Devices[snapshot.Endpoint.Id] = profile;
        }

        profile.Name = snapshot.Endpoint.Name;
        profile.MasterVolume = Clamp(snapshot.Endpoint.MasterVolume);
        profile.MasterMuted = snapshot.Endpoint.IsMuted;
        profile.LastUpdatedUtc = now;

        foreach (var appGroup in snapshot.Sessions.GroupBy(
                     session => session.ApplicationKey,
                     StringComparer.OrdinalIgnoreCase))
        {
            var session = appGroup.First();
            profile.Applications[session.ApplicationKey] = new ApplicationVolumeProfile
            {
                ApplicationKey = session.ApplicationKey,
                DisplayName = session.DisplayName,
                ExecutablePath = session.ExecutablePath,
                Volume = Clamp(session.Volume),
                Muted = session.IsMuted,
                LastUpdatedUtc = now
            };
        }
    }

    private void Restore(string deviceId)
    {
        if (!_document.Devices.TryGetValue(deviceId, out var profile)) return;

        _isRestoring = true;
        try
        {
            audio.SetMasterVolume(
                deviceId,
                Clamp(profile.MasterVolume),
                _document.Settings.SaveMuteState ? profile.MasterMuted : null);

            foreach (var app in profile.Applications.Values)
            {
                audio.SetApplicationVolume(
                    deviceId,
                    app.ApplicationKey,
                    Clamp(app.Volume),
                    _document.Settings.SaveMuteState ? app.Muted : null);
            }
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void RestoreApplication(string applicationKey)
    {
        var deviceId = audio.GetDefaultRenderDeviceId();
        if (deviceId is null ||
            !_document.Devices.TryGetValue(deviceId, out var profile) ||
            !profile.Applications.TryGetValue(applicationKey, out var app))
        {
            return;
        }

        _isRestoring = true;
        try
        {
            audio.SetApplicationVolume(
                deviceId,
                app.ApplicationKey,
                Clamp(app.Volume),
                _document.Settings.SaveMuteState ? app.Muted : null);
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private static float Clamp(float volume) => Math.Clamp(volume, 0f, 1f);

    private void MarkSaved()
    {
        RaiseSaveState(ProfileSaveState.Saved);
    }

    private void RaiseSaveState(ProfileSaveState state, Exception? exception = null)
    {
        if (_disposed) return;
        ProfileSaveStateChanged?.Invoke(this, new ProfileSaveStateChangedEventArgs(state, exception));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        audio.StateChanged -= OnAudioStateChanged;
        _audioEvents.Writer.TryComplete();
        _saveRequests.Writer.TryComplete();
        _renderDeviceReconcileShutdown.Cancel();
        Task? renderDeviceReconcileTask;
        lock (_renderDeviceReconcileGate)
        {
            renderDeviceReconcileTask = _renderDeviceReconcileTask;
        }

        try
        {
            renderDeviceReconcileTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        try
        {
            _audioEventWorker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _audioEventWorkerShutdown.Cancel();
        _saveWorkerShutdown.Cancel();
        try
        {
            _saveWorker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _audioEventWorkerShutdown.Dispose();
        _saveWorkerShutdown.Dispose();
        _renderDeviceReconcileShutdown.Dispose();
        _renderDeviceReconcileWakeSignal.Dispose();
        audio.Dispose();
    }
}
