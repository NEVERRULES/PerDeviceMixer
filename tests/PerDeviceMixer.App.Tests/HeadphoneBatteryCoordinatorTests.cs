using PerDeviceMixer.App;
using PerDeviceMixer.Bluetooth;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App.Tests;

public sealed class HeadphoneBatteryCoordinatorTests
{
    private const string BeatsHardwareId =
        "{1}.BTHENUM\\service_VID&0001004C_PID&2012\\device";

    [Fact]
    public async Task StartsMonitorOnlyForSupportedCurrentEndpoint()
    {
        var monitor = new FakeHeadphoneBatteryMonitor();
        using var engine = CreateEngine(BeatsHardwareId);
        await engine.InitializeAsync();
        using var coordinator = new HeadphoneBatteryCoordinator(engine, monitor);

        coordinator.Start();

        Assert.True(monitor.IsRunning);
        Assert.Equal(AppleHeadphoneIds.BeatsFitProProductId, monitor.ProductId);
        Assert.True(coordinator.CurrentStatus.IsSupportedDeviceActive);
    }

    [Fact]
    public async Task DisablingSettingStopsMonitorAndClearsStatus()
    {
        var monitor = new FakeHeadphoneBatteryMonitor();
        using var engine = CreateEngine(BeatsHardwareId);
        await engine.InitializeAsync();
        using var coordinator = new HeadphoneBatteryCoordinator(engine, monitor);
        coordinator.Start();
        engine.UpdateSettings(settings => settings.ShowSupportedHeadphoneBattery = false);

        coordinator.Reconcile();

        Assert.False(monitor.IsRunning);
        Assert.False(coordinator.CurrentStatus.IsSupportedDeviceActive);
    }

    [Fact]
    public async Task PublishesParsedBatteryForActiveSupportedEndpoint()
    {
        var monitor = new FakeHeadphoneBatteryMonitor();
        using var engine = CreateEngine(BeatsHardwareId);
        await engine.InitializeAsync();
        using var coordinator = new HeadphoneBatteryCoordinator(engine, monitor);
        coordinator.Start();
        var battery = new HeadphoneBatteryState(
            AppleHeadphoneIds.BeatsFitProProductId,
            "Beats Fit Pro",
            80,
            70,
            60,
            false,
            false,
            true,
            HeadphoneSide.Left,
            -40,
            DateTimeOffset.UnixEpoch);

        monitor.Publish(battery);

        Assert.Equal(battery, coordinator.CurrentStatus.Battery);
    }

    private static MixerEngine CreateEngine(string? hardwareInstanceId)
    {
        var endpoint = new AudioEndpointInfo(
            "device",
            "Headphones",
            true,
            0.4f,
            false,
            hardwareInstanceId);
        return new MixerEngine(
            new FakeAudioService(new MixerSnapshot(endpoint, [])),
            new MemoryProfileStore());
    }

    private sealed class FakeHeadphoneBatteryMonitor : IHeadphoneBatteryMonitor
    {
        public event EventHandler<HeadphoneBatteryStateChangedEventArgs>? StateChanged;
        public event EventHandler<HeadphoneMonitorErrorEventArgs>? MonitoringFailed
        {
            add { }
            remove { }
        }

        public bool IsRunning { get; private set; }
        public HeadphoneBatteryState? CurrentState { get; private set; }
        public ushort ProductId { get; private set; }

        public void Start(ushort productId)
        {
            ProductId = productId;
            IsRunning = true;
        }

        public void StopMonitoring()
        {
            IsRunning = false;
            CurrentState = null;
        }

        public void Publish(HeadphoneBatteryState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new HeadphoneBatteryStateChangedEventArgs(state));
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }

    private sealed class FakeAudioService(MixerSnapshot snapshot) : IAudioService
    {
        public event EventHandler<AudioStateChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<AudioEndpointInfo> GetRenderDevices() => [snapshot.Endpoint];
        public IReadOnlyList<AudioEndpointInfo> GetCaptureDevices() => [];
        public string? GetDefaultRenderDeviceId() => snapshot.Endpoint.Id;
        public string? GetDefaultCaptureDeviceId() => null;
        public MixerSnapshot? GetDefaultMixerSnapshot() => snapshot;
        public void SetMasterVolume(string deviceId, float volume, bool? muted = null) { }
        public void SetApplicationVolume(
            string deviceId,
            string applicationKey,
            float volume,
            bool? muted = null)
        { }
        public void SetDefaultDevice(string deviceId, AudioDeviceDirection direction) { }
        public void StartMonitoring() { }
        public void Dispose() { }
    }

    private sealed class MemoryProfileStore : IProfileStore
    {
        private MixerProfileDocument _document = new();
        public string FilePath => "memory://profiles.json";
        public Task<MixerProfileDocument> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_document);
        public void Save(MixerProfileDocument document) => _document = document;
        public Task SaveAsync(
            MixerProfileDocument document,
            CancellationToken cancellationToken = default)
        {
            _document = document;
            return Task.CompletedTask;
        }
    }
}
