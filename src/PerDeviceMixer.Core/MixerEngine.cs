namespace PerDeviceMixer.Core;

public sealed class MixerEngine(IAudioService audio, IProfileStore profileStore) : IDisposable
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private CancellationTokenSource? _saveDebounce;
    private MixerProfileDocument _document = new();
    private bool _isRestoring;
    private bool _disposed;

    public event EventHandler<AudioStateChangedEventArgs>? MixerChanged;

    public MixerProfileDocument Profiles => _document;
    public string ProfilePath => profileStore.FilePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _document = await profileStore.LoadAsync(cancellationToken);
        audio.StateChanged += OnAudioStateChanged;
        audio.StartMonitoring();

        var snapshot = await GetSnapshotWithRetryAsync(5, cancellationToken);
        if (snapshot is null)
        {
            await profileStore.SaveAsync(_document, cancellationToken);
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

    public void SetDefaultOutputDevice(string deviceId) =>
        audio.SetDefaultDevice(deviceId, AudioDeviceDirection.Output);

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
            profileStore.Save(_document);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _saveDebounce?.Cancel();
        var snapshot = await GetSnapshotWithRetryAsync(3, cancellationToken);
        if (snapshot is not null)
        {
            await CaptureAndSaveAsync(snapshot, cancellationToken);
        }
        else
        {
            await profileStore.SaveAsync(_document, cancellationToken);
        }
    }

    public void Flush()
    {
        _saveDebounce?.Cancel();
        var snapshot = audio.GetDefaultMixerSnapshot();
        if (snapshot is not null)
        {
            Capture(snapshot);
        }
        else
        {
            profileStore.Save(_document);
        }
    }

    private void Capture(MixerSnapshot snapshot)
    {
        _operationLock.Wait();
        try
        {
            UpdateDocument(snapshot);
            profileStore.Save(_document);
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
        _ = HandleAudioStateChangedAsync(args);
    }

    private async Task HandleAudioStateChangedAsync(AudioStateChangedEventArgs args)
    {
        if (_disposed) return;

        try
        {
            if (args.Kind == AudioChangeKind.DefaultDevice)
            {
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
        }
        catch
        {
            // A transient endpoint disappearing during a Bluetooth/HDMI switch is expected.
            // The next Core Audio notification refreshes the state.
        }
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
        _saveDebounce?.Cancel();
        _saveDebounce?.Dispose();
        _saveDebounce = new CancellationTokenSource();
        var token = _saveDebounce.Token;
        var delay = Math.Clamp(_document.Settings.SaveDebounceMilliseconds, 100, 5000);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                await SaveDocumentAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        audio.StateChanged -= OnAudioStateChanged;
        _saveDebounce?.Cancel();
        _saveDebounce?.Dispose();
        audio.Dispose();
    }
}
