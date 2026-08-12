using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.Audio;

public sealed class CoreAudioService : IAudioService
{
    private static readonly Guid EventContext = new("F464F931-E6D8-42F4-9BF1-4BC2DF2D08A8");
    private const int MaximumSuppressedApplications = 256;

    private readonly AudioThreadDispatcher _dispatcher;
    private readonly DeviceNotificationClient _deviceNotifications;
    private readonly List<TrackedSession> _trackedSessions = [];
    private readonly Dictionary<string, DateTime> _suppressedApplicationEvents =
        new(StringComparer.OrdinalIgnoreCase);
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _monitoredRenderDevice;
    private MMDevice? _monitoredCaptureDevice;
    private AudioSessionManager? _sessionManager;
    private string? _currentDeviceId;
    private string? _currentCaptureDeviceId;
    private bool _monitoring;
    private bool _disposed;

    public CoreAudioService()
    {
        _deviceNotifications = new DeviceNotificationClient(this);
        _dispatcher = new AudioThreadDispatcher(() => _enumerator = new MMDeviceEnumerator());
    }

    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    private MMDeviceEnumerator Enumerator =>
        _enumerator ?? throw new InvalidOperationException("Core Audio is not initialized.");

    public IReadOnlyList<AudioEndpointInfo> GetRenderDevices()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(GetRenderDevicesCore);
    }

    public IReadOnlyList<AudioEndpointInfo> GetCaptureDevices()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(GetCaptureDevicesCore);
    }

    public string? GetDefaultRenderDeviceId()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(GetDefaultRenderDeviceIdCore);
    }

    public string? GetDefaultCaptureDeviceId()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(GetDefaultCaptureDeviceIdCore);
    }

    public MixerSnapshot? GetDefaultMixerSnapshot()
    {
        ThrowIfDisposed();
        return _dispatcher.Invoke(GetDefaultMixerSnapshotCore);
    }

    public void SetMasterVolume(string deviceId, float volume, bool? muted = null)
    {
        ThrowIfDisposed();
        _dispatcher.Invoke(() => SetMasterVolumeCore(deviceId, volume, muted));
    }

    public void SetApplicationVolume(
        string deviceId,
        string applicationKey,
        float volume,
        bool? muted = null)
    {
        ThrowIfDisposed();
        _dispatcher.Invoke(() => SetApplicationVolumeCore(deviceId, applicationKey, volume, muted));
    }

    public void SetDefaultDevice(string deviceId, AudioDeviceDirection direction)
    {
        ThrowIfDisposed();
        _dispatcher.Invoke(() => SetDefaultDeviceCore(deviceId, direction));
    }

    public void StartMonitoring()
    {
        ThrowIfDisposed();
        _dispatcher.Invoke(StartMonitoringCore);
    }

    private IReadOnlyList<AudioEndpointInfo> GetRenderDevicesCore()
    {
        var defaultId = GetDefaultRenderDeviceIdCore();
        var result = new List<AudioEndpointInfo>();
        var devices = Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var index = 0; index < devices.Count; index++)
        {
            using var device = devices[index];
            result.Add(CreateEndpointInfo(
                device,
                string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    private IReadOnlyList<AudioEndpointInfo> GetCaptureDevicesCore()
    {
        var defaultId = GetDefaultCaptureDeviceIdCore();
        var result = new List<AudioEndpointInfo>();
        var devices = Enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        for (var index = 0; index < devices.Count; index++)
        {
            using var device = devices[index];
            result.Add(CreateEndpointInfo(
                device,
                string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    private string? GetDefaultRenderDeviceIdCore()
    {
        if (!Enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return null;
        using var device = Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return device.ID;
    }

    private string? GetDefaultCaptureDeviceIdCore()
    {
        if (!Enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)) return null;
        using var device = Enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        return device.ID;
    }

    private MixerSnapshot? GetDefaultMixerSnapshotCore()
    {
        if (!Enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return null;
        using var device = Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var endpoint = CreateEndpointInfo(device, true);
        return new MixerSnapshot(endpoint, EnumerateSessions(device));
    }

    private void SetMasterVolumeCore(string deviceId, float volume, bool? muted)
    {
        using var device = Enumerator.GetDevice(deviceId);
        var endpointVolume = device.AudioEndpointVolume;
        endpointVolume.NotificationGuid = EventContext;
        endpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
        if (muted.HasValue) endpointVolume.Mute = muted.Value;
    }

    private void SetApplicationVolumeCore(
        string deviceId,
        string applicationKey,
        float volume,
        bool? muted)
    {
        SuppressApplicationEvents(applicationKey);
        using var device = Enumerator.GetDevice(deviceId);
        var manager = device.AudioSessionManager;
        var sessions = manager.Sessions;
        try
        {
            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                var identity = ApplicationIdentity.Create(session);
                if (!string.Equals(identity.ApplicationKey, applicationKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                if (muted.HasValue) session.SimpleAudioVolume.Mute = muted.Value;
            }
        }
        finally
        {
            manager.Dispose();
        }
    }

    private void SetDefaultDeviceCore(string deviceId, AudioDeviceDirection direction)
    {
        using var device = Enumerator.GetDevice(deviceId);
        var expectedFlow = direction == AudioDeviceDirection.Output ? DataFlow.Render : DataFlow.Capture;
        if (device.DataFlow != expectedFlow)
        {
            throw new InvalidOperationException("Audio endpoint direction does not match the requested default role.");
        }

        PolicyConfig.SetDefaultEndpoint(deviceId);
    }

    private void StartMonitoringCore()
    {
        if (_monitoring) return;
        var result = Enumerator.RegisterEndpointNotificationCallback(_deviceNotifications);
        if (result != 0) System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(result);
        _monitoring = true;
        AttachToDefaultRenderDeviceCore();
        AttachToDefaultCaptureDeviceCore();
    }

    private static AudioEndpointInfo CreateEndpointInfo(MMDevice device, bool isDefault)
    {
        var endpointVolume = device.AudioEndpointVolume;
        return new AudioEndpointInfo(
            device.ID,
            string.IsNullOrWhiteSpace(device.FriendlyName) ? device.DeviceFriendlyName : device.FriendlyName,
            isDefault,
            Math.Clamp(endpointVolume.MasterVolumeLevelScalar, 0f, 1f),
            endpointVolume.Mute);
    }

    private static List<AudioSessionInfo> EnumerateSessions(MMDevice device)
    {
        var result = new List<AudioSessionInfo>();
        var manager = device.AudioSessionManager;
        var sessions = manager.Sessions;
        try
        {
            for (var index = 0; index < sessions.Count; index++)
            {
                using var session = sessions[index];
                result.Add(ApplicationIdentity.Create(session));
            }
        }
        finally
        {
            manager.Dispose();
        }

        return result;
    }

    private void AttachToDefaultRenderDeviceCore()
    {
        DetachFromCurrentRenderDeviceCore();
        if (!Enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            _currentDeviceId = null;
            return;
        }

        _monitoredRenderDevice = Enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _currentDeviceId = _monitoredRenderDevice.ID;
        _monitoredRenderDevice.AudioEndpointVolume.NotificationGuid = EventContext;
        _monitoredRenderDevice.AudioEndpointVolume.OnVolumeNotification += OnMasterVolumeChanged;

        _sessionManager = _monitoredRenderDevice.AudioSessionManager;
        _sessionManager.OnSessionCreated += OnSessionCreated;
        var sessions = _sessionManager.Sessions;
        for (var index = 0; index < sessions.Count; index++) TrackSessionCore(sessions[index]);
    }

    private void AttachToDefaultCaptureDeviceCore()
    {
        DetachFromCurrentCaptureDeviceCore();
        if (!Enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
        {
            _currentCaptureDeviceId = null;
            return;
        }

        _monitoredCaptureDevice = Enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        _currentCaptureDeviceId = _monitoredCaptureDevice.ID;
        _monitoredCaptureDevice.AudioEndpointVolume.NotificationGuid = EventContext;
        _monitoredCaptureDevice.AudioEndpointVolume.OnVolumeNotification += OnCaptureVolumeChanged;
    }

    private void TrackSessionCore(AudioSessionControl session)
    {
        var identity = ApplicationIdentity.Create(session);
        if (_trackedSessions.Any(item => string.Equals(
                item.SessionId,
                identity.SessionId,
                StringComparison.OrdinalIgnoreCase)))
        {
            session.Dispose();
            return;
        }

        var handler = new SessionEventsHandler(
            (volume, muted) => _dispatcher.Post(() =>
                HandleSessionVolumeChanged(identity.ApplicationKey, volume, muted)),
            () => _dispatcher.Post(() => RemoveExpiredSessionCore(identity.SessionId)));
        session.RegisterEventClient(handler);
        _trackedSessions.Add(new TrackedSession(identity.SessionId, session, handler));
    }

    private void HandleSessionVolumeChanged(string applicationKey, float volume, bool muted)
    {
        if (IsApplicationEventSuppressed(applicationKey)) return;
        RaiseStateChanged(AudioChangeKind.SessionVolume, applicationKey, volume, muted);
    }

    private void RemoveExpiredSessionCore(string sessionId)
    {
        var tracked = _trackedSessions.FirstOrDefault(item =>
            string.Equals(item.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        if (tracked is null) return;
        _trackedSessions.Remove(tracked);
        tracked.Dispose();
    }

    private void OnMasterVolumeChanged(AudioVolumeNotificationData data)
    {
        if (data.EventContext == EventContext) return;
        _dispatcher.Post(() => RaiseStateChanged(
            AudioChangeKind.MasterVolume,
            volume: data.MasterVolume,
            isMuted: data.Muted));
    }

    private void OnCaptureVolumeChanged(AudioVolumeNotificationData data)
    {
        if (data.EventContext == EventContext) return;
        _dispatcher.Post(() => StateChanged?.Invoke(
            this,
            new AudioStateChangedEventArgs(
                AudioChangeKind.CaptureVolume,
                _currentCaptureDeviceId,
                volume: data.MasterVolume,
                isMuted: data.Muted)));
    }

    private void OnSessionCreated(object sender, IAudioSessionControl newSession)
    {
        _dispatcher.Post(() =>
        {
            var session = new AudioSessionControl(newSession);
            var identity = ApplicationIdentity.Create(session);
            TrackSessionCore(session);
            RaiseStateChanged(AudioChangeKind.SessionCreated, identity.ApplicationKey);
        });
    }

    private void HandleDefaultRenderDeviceChangedCore(string? deviceId)
    {
        AttachToDefaultRenderDeviceCore();
        RaiseStateChanged(AudioChangeKind.DefaultDevice, deviceId: deviceId);
    }

    private void HandleDefaultCaptureDeviceChangedCore(string? deviceId)
    {
        AttachToDefaultCaptureDeviceCore();
        StateChanged?.Invoke(
            this,
            new AudioStateChangedEventArgs(AudioChangeKind.DefaultCaptureDevice, deviceId));
    }

    private void RaiseStateChanged(
        AudioChangeKind kind,
        string? applicationKey = null,
        float? volume = null,
        bool? isMuted = null,
        string? deviceId = null) =>
        StateChanged?.Invoke(
            this,
            new AudioStateChangedEventArgs(
                kind,
                deviceId ?? _currentDeviceId,
                applicationKey,
                volume,
                isMuted));

    private void SuppressApplicationEvents(string applicationKey)
    {
        var now = DateTime.UtcNow;
        PruneSuppressedApplicationEvents(now);
        if (_suppressedApplicationEvents.Count >= MaximumSuppressedApplications &&
            !_suppressedApplicationEvents.ContainsKey(applicationKey))
        {
            var oldest = _suppressedApplicationEvents.MinBy(item => item.Value);
            if (!string.IsNullOrEmpty(oldest.Key)) _suppressedApplicationEvents.Remove(oldest.Key);
        }

        _suppressedApplicationEvents[applicationKey] = now.AddMilliseconds(350);
    }

    private bool IsApplicationEventSuppressed(string applicationKey)
    {
        if (!_suppressedApplicationEvents.TryGetValue(applicationKey, out var until)) return false;
        if (until >= DateTime.UtcNow) return true;
        _suppressedApplicationEvents.Remove(applicationKey);
        return false;
    }

    private void PruneSuppressedApplicationEvents(DateTime now)
    {
        if (_suppressedApplicationEvents.Count == 0) return;
        foreach (var key in _suppressedApplicationEvents
                     .Where(item => item.Value < now)
                     .Select(item => item.Key)
                     .ToArray())
        {
            _suppressedApplicationEvents.Remove(key);
        }
    }

    private void DetachFromCurrentRenderDeviceCore()
    {
        if (_sessionManager is not null)
        {
            _sessionManager.OnSessionCreated -= OnSessionCreated;
            _sessionManager.Dispose();
            _sessionManager = null;
        }

        foreach (var session in _trackedSessions) session.Dispose();
        _trackedSessions.Clear();

        if (_monitoredRenderDevice is not null)
        {
            _monitoredRenderDevice.AudioEndpointVolume.OnVolumeNotification -= OnMasterVolumeChanged;
            _monitoredRenderDevice.Dispose();
            _monitoredRenderDevice = null;
        }
    }

    private void DetachFromCurrentCaptureDeviceCore()
    {
        if (_monitoredCaptureDevice is null) return;
        _monitoredCaptureDevice.AudioEndpointVolume.OnVolumeNotification -= OnCaptureVolumeChanged;
        _monitoredCaptureDevice.Dispose();
        _monitoredCaptureDevice = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _dispatcher.Invoke(() =>
            {
                if (_monitoring)
                {
                    Enumerator.UnregisterEndpointNotificationCallback(_deviceNotifications);
                    _monitoring = false;
                }

                DetachFromCurrentRenderDeviceCore();
                DetachFromCurrentCaptureDeviceCore();
                Enumerator.Dispose();
                _enumerator = null;
            });
        }
        finally
        {
            _dispatcher.Dispose();
        }
    }

    private sealed record TrackedSession(
        string SessionId,
        AudioSessionControl Session,
        SessionEventsHandler Handler) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                Session.UnRegisterEventClient(Handler);
            }
            catch
            {
            }
            Session.Dispose();
        }
    }

    private sealed class DeviceNotificationClient(CoreAudioService owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            owner._dispatcher.Post(() => owner.RaiseStateChanged(
                AudioChangeKind.DeviceCollection,
                deviceId: deviceId));

        public void OnDeviceAdded(string pwstrDeviceId) =>
            owner._dispatcher.Post(() => owner.RaiseStateChanged(
                AudioChangeKind.DeviceCollection,
                deviceId: pwstrDeviceId));

        public void OnDeviceRemoved(string deviceId) =>
            owner._dispatcher.Post(() => owner.RaiseStateChanged(
                AudioChangeKind.DeviceCollection,
                deviceId: deviceId));

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (role != Role.Multimedia) return;

            if (flow == DataFlow.Render)
            {
                owner._dispatcher.Post(() => owner.HandleDefaultRenderDeviceChangedCore(defaultDeviceId));
            }
            else if (flow == DataFlow.Capture)
            {
                owner._dispatcher.Post(() => owner.HandleDefaultCaptureDeviceChangedCore(defaultDeviceId));
            }
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    private sealed class SessionEventsHandler(
        Action<float, bool> volumeChanged,
        Action disconnected) : IAudioSessionEventsHandler
    {
        public void OnVolumeChanged(float volume, bool isMuted) => volumeChanged(volume, isMuted);
        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state)
        {
            if (state == AudioSessionState.AudioSessionStateExpired) disconnected();
        }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => disconnected();
    }
}
