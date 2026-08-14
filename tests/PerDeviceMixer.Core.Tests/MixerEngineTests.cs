using PerDeviceMixer.Core;

namespace PerDeviceMixer.Core.Tests;

public sealed class MixerEngineTests
{
    [Fact]
    public async Task InitializeAsyncCapturesAnUnknownDeviceWithoutChangingAudio()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);

        await engine.InitializeAsync();

        var saved = await store.LoadAsync();
        Assert.Equal(0.42f, saved.Devices["device-new"].MasterVolume);
        Assert.Equal(0.67f, saved.Devices["device-new"].Applications["exe:test.exe"].Volume);
        Assert.Empty(audio.MasterChanges);
        Assert.Empty(audio.ApplicationChanges);
    }

    [Fact]
    public async Task InitializeAsyncRestoresKnownDeviceAndApplication()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        await store.SaveAsync(CreateSavedDocument());
        var audio = new FakeAudioService(CreateSnapshot("device-known", 0.9f, 0.8f));
        using var engine = new MixerEngine(audio, store);

        await engine.InitializeAsync();

        var masterChange = Assert.Single(audio.MasterChanges);
        Assert.Equal(0.25f, masterChange.Volume);
        var appChange = Assert.Single(audio.ApplicationChanges);
        Assert.Equal("exe:test.exe", appChange.ApplicationKey);
        Assert.Equal(0.30f, appChange.Volume);
    }

    [Fact]
    public async Task SessionCreatedRestoresKnownAppButLeavesUnknownAppAlone()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        await store.SaveAsync(CreateSavedDocument());
        var audio = new FakeAudioService(CreateSnapshot("device-known", 0.25f, 0.30f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();
        audio.ApplicationChanges.Clear();

        audio.RaiseSessionCreated("exe:test.exe");
        audio.RaiseSessionCreated("exe:unknown.exe");
        await Task.Delay(50);

        var change = Assert.Single(audio.ApplicationChanges);
        Assert.Equal("exe:test.exe", change.ApplicationKey);
        Assert.Equal(0.30f, change.Volume);
    }

    [Fact]
    public async Task FlushPreservesSavedApplicationsThatAreNotCurrentlyRunning()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var document = CreateSavedDocument();
        document.Devices["device-known"].Applications["exe:offline.exe"] = new ApplicationVolumeProfile
        {
            ApplicationKey = "exe:offline.exe",
            DisplayName = "Offline",
            Volume = 0.45f,
            Muted = false,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };
        await store.SaveAsync(document);
        var audio = new FakeAudioService(CreateSnapshot("device-known", 0.25f, 0.30f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();

        engine.Flush();

        var loaded = await store.LoadAsync();
        Assert.Contains("exe:offline.exe", loaded.Devices["device-known"].Applications);
    }

    [Fact]
    public async Task LocalSliderChangesImmediatelyUpdateTheActiveProfile()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();

        engine.SetMasterVolume(0.55f);
        engine.SetApplicationVolume("exe:test.exe", 0.66f);

        Assert.Equal(0.55f, engine.Profiles.Devices["device-new"].MasterVolume);
        Assert.Equal(0.66f, engine.Profiles.Devices["device-new"].Applications["exe:test.exe"].Volume);
    }

    [Fact]
    public async Task LocalMasterChangeRaisesMixerChangedForTrayStatus()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();
        AudioStateChangedEventArgs? observed = null;
        engine.MixerChanged += (_, eventArgs) => observed = eventArgs;

        engine.SetMasterVolume(0.55f, true);

        Assert.NotNull(observed);
        Assert.Equal(AudioChangeKind.MasterVolume, observed.Kind);
        Assert.Equal("device-new", observed.DeviceId);
        Assert.Equal(0.55f, observed.Volume);
        Assert.True(observed.IsMuted);
    }

    [Fact]
    public async Task ExternalMasterNotificationUpdatesProfileWithoutReenumeratingSessions()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();
        var readsBeforeNotification = audio.SnapshotReadCount;

        audio.RaiseMasterVolumeChanged(0.73f, true);
        await Task.Delay(50);

        Assert.Equal(readsBeforeNotification, audio.SnapshotReadCount);
        Assert.Equal(0.73f, engine.Profiles.Devices["device-new"].MasterVolume);
        Assert.True(engine.Profiles.Devices["device-new"].MasterMuted);
    }

    [Fact]
    public async Task DeviceSelectionAndInputVolumeAreForwardedToAudioService()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();

        engine.SetDefaultOutputDevice("device-new");
        engine.SetDefaultInputDevice("capture-device");
        engine.SetInputVolume(0.44f, true);

        Assert.Contains(("device-new", AudioDeviceDirection.Output), audio.DefaultDeviceChanges);
        Assert.Contains(("capture-device", AudioDeviceDirection.Input), audio.DefaultDeviceChanges);
        Assert.Contains(audio.MasterChanges, change =>
            change.DeviceId == "capture-device" && change.Volume == 0.44f && change.Muted == true);
    }

    [Fact]
    public async Task SettingsAreAutomaticallySavedAfterTheDebounceInterval()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();

        engine.UpdateSettings(settings =>
        {
            settings.SaveDebounceMilliseconds = 100;
            settings.AutomaticUpdateChecks = false;
            settings.UpdateCheckIntervalHours = 72;
        });

        await WaitUntilAsync(async () =>
        {
            var loaded = await store.LoadAsync();
            return !loaded.Settings.AutomaticUpdateChecks && loaded.Settings.UpdateCheckIntervalHours == 72;
        });
    }

    [Fact]
    public async Task AutomaticSavePublishesFailedStateWhenTheStoreWriteFails()
    {
        var store = new FailingAfterInitializationProfileStore(CreateSavedDocument());
        var audio = new FakeAudioService(CreateSnapshot("device-known", 0.25f, 0.30f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();
        var failure = new TaskCompletionSource<ProfileSaveStateChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ProfileSaveStateChanged += (_, eventArgs) =>
        {
            if (eventArgs.State == ProfileSaveState.Failed) failure.TrySetResult(eventArgs);
        };

        engine.UpdateSettings(settings => settings.SaveDebounceMilliseconds = 100);

        var failedState = await failure.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsType<InvalidOperationException>(failedState.Exception);
    }

    [Fact]
    public async Task ConcurrentVolumeNotificationsAreSerializedAndFlushed()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();

        Parallel.For(0, 64, index =>
            audio.RaiseMasterVolumeChanged(index / 100f, index % 2 == 0));
        audio.RaiseMasterVolumeChanged(0.73f, true);

        await WaitUntilAsync(() => Task.FromResult(
            engine.Profiles.Devices["device-new"].MasterVolume == 0.73f &&
            engine.Profiles.Devices["device-new"].MasterMuted));
        await engine.FlushAsync();

        var loaded = await store.LoadAsync();
        Assert.Equal(0.73f, loaded.Devices["device-new"].MasterVolume);
        Assert.True(loaded.Devices["device-new"].MasterMuted);
    }

    [Fact]
    public async Task AudioEventsAreProcessedInArrivalOrder()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        using var engine = new MixerEngine(audio, store);
        await engine.InitializeAsync();
        var observedVolumes = new List<float>();
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.MixerChanged += (_, eventArgs) =>
        {
            if (eventArgs.Kind != AudioChangeKind.MasterVolume || !eventArgs.Volume.HasValue) return;
            observedVolumes.Add(eventArgs.Volume.Value);
            if (observedVolumes.Count == 3) completed.TrySetResult();
        };

        audio.RaiseMasterVolumeChanged(0.11f, false);
        audio.RaiseMasterVolumeChanged(0.22f, false);
        audio.RaiseMasterVolumeChanged(0.33f, true);

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal([0.11f, 0.22f, 0.33f], observedVolumes);
        Assert.Equal(0.33f, engine.Profiles.Devices["device-new"].MasterVolume);
        Assert.True(engine.Profiles.Devices["device-new"].MasterMuted);
    }

    [Fact]
    public async Task EnabledAudioDiagnosticsRecordOnlyHashedEventIdentity()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var audio = new FakeAudioService(CreateSnapshot("device-new", 0.42f, 0.67f));
        var diagnostics = new CollectingDiagnosticSink();
        using var engine = new MixerEngine(audio, store, diagnostics);
        await engine.InitializeAsync();
        engine.UpdateSettings(settings => settings.AudioDiagnosticsEnabled = true);

        audio.RaiseMasterVolumeChanged(0.73f, true);
        await WaitUntilAsync(() => Task.FromResult(diagnostics.Snapshot().Any(entry =>
            entry.Kind == AudioChangeKind.MasterVolume && entry.Stage == "completed")));

        var entry = Assert.Single(
            diagnostics.Snapshot(),
            item => item.Kind == AudioChangeKind.MasterVolume && item.Stage == "completed");
        Assert.NotNull(entry.DeviceToken);
        Assert.Equal(12, entry.DeviceToken!.Length);
        Assert.DoesNotContain("device-new", entry.DeviceToken, StringComparison.OrdinalIgnoreCase);
        Assert.Null(entry.ApplicationToken);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < timeout)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }

        Assert.Fail("The expected persisted state was not observed before the timeout.");
    }

    private static MixerProfileDocument CreateSavedDocument()
    {
        var document = new MixerProfileDocument();
        document.Devices["device-known"] = new DeviceProfile
        {
            DeviceId = "device-known",
            Name = "Known speakers",
            MasterVolume = 0.25f,
            MasterMuted = false,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            Applications =
            {
                ["exe:test.exe"] = new ApplicationVolumeProfile
                {
                    ApplicationKey = "exe:test.exe",
                    DisplayName = "Test",
                    Volume = 0.30f,
                    Muted = false,
                    LastUpdatedUtc = DateTimeOffset.UtcNow
                }
            }
        };
        return document;
    }

    private static MixerSnapshot CreateSnapshot(string deviceId, float master, float app) => new(
        new AudioEndpointInfo(deviceId, "Test device", true, master, false),
        [new AudioSessionInfo("session-1", "exe:test.exe", "Test", null, 42, false, app, false)]);

    private sealed class FailingAfterInitializationProfileStore(MixerProfileDocument document) : IProfileStore
    {
        private int _saveCount;

        public string FilePath => "failure-test.json";

        public Task<MixerProfileDocument> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(document);

        public void Save(MixerProfileDocument value)
        {
            if (Interlocked.Increment(ref _saveCount) > 1)
            {
                throw new InvalidOperationException("Simulated profile write failure.");
            }
        }

        public Task SaveAsync(
            MixerProfileDocument value,
            CancellationToken cancellationToken = default)
        {
            Save(value);
            return Task.CompletedTask;
        }
    }

    private sealed class CollectingDiagnosticSink : IAudioDiagnosticSink
    {
        public List<AudioDiagnosticEntry> Entries { get; } = [];

        public void Write(AudioDiagnosticEntry entry)
        {
            lock (Entries) Entries.Add(entry);
        }

        public AudioDiagnosticEntry[] Snapshot()
        {
            lock (Entries) return Entries.ToArray();
        }
    }

    private sealed class FakeAudioService(MixerSnapshot snapshot) : IAudioService
    {
        private MixerSnapshot _snapshot = snapshot;

        public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

        public List<(string DeviceId, float Volume, bool? Muted)> MasterChanges { get; } = [];
        public List<(string DeviceId, string ApplicationKey, float Volume, bool? Muted)> ApplicationChanges { get; } = [];
        public List<(string DeviceId, AudioDeviceDirection Direction)> DefaultDeviceChanges { get; } = [];
        public int SnapshotReadCount { get; private set; }

        public IReadOnlyList<AudioEndpointInfo> GetRenderDevices() => [_snapshot.Endpoint];
        public IReadOnlyList<AudioEndpointInfo> GetCaptureDevices() =>
            [new AudioEndpointInfo("capture-device", "Test microphone", true, 0.5f, false)];
        public string? GetDefaultRenderDeviceId() => _snapshot.Endpoint.Id;
        public string? GetDefaultCaptureDeviceId() => "capture-device";
        public MixerSnapshot? GetDefaultMixerSnapshot()
        {
            SnapshotReadCount++;
            return _snapshot;
        }

        public void SetMasterVolume(string deviceId, float volume, bool? muted = null)
        {
            MasterChanges.Add((deviceId, volume, muted));
            _snapshot = _snapshot with
            {
                Endpoint = _snapshot.Endpoint with
                {
                    MasterVolume = volume,
                    IsMuted = muted ?? _snapshot.Endpoint.IsMuted
                }
            };
        }

        public void SetApplicationVolume(string deviceId, string applicationKey, float volume, bool? muted = null)
        {
            ApplicationChanges.Add((deviceId, applicationKey, volume, muted));
            _snapshot = _snapshot with
            {
                Sessions = _snapshot.Sessions.Select(session =>
                    string.Equals(session.ApplicationKey, applicationKey, StringComparison.OrdinalIgnoreCase)
                        ? session with { Volume = volume, IsMuted = muted ?? session.IsMuted }
                        : session).ToList()
            };
        }

        public void SetDefaultDevice(string deviceId, AudioDeviceDirection direction) =>
            DefaultDeviceChanges.Add((deviceId, direction));

        public void StartMonitoring() { }

        public void RaiseSessionCreated(string applicationKey) => StateChanged?.Invoke(
            this,
            new AudioStateChangedEventArgs(AudioChangeKind.SessionCreated, _snapshot.Endpoint.Id, applicationKey));

        public void RaiseMasterVolumeChanged(float volume, bool muted)
        {
            _snapshot = _snapshot with
            {
                Endpoint = _snapshot.Endpoint with
                {
                    MasterVolume = volume,
                    IsMuted = muted
                }
            };
            StateChanged?.Invoke(
                this,
                new AudioStateChangedEventArgs(
                    AudioChangeKind.MasterVolume,
                    _snapshot.Endpoint.Id,
                    volume: volume,
                    isMuted: muted));
        }

        public void Dispose() { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PerDeviceMixer.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
