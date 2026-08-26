namespace PerDeviceMixer.Core;

public sealed record AudioEndpointInfo(
    string Id,
    string Name,
    bool IsDefault,
    float MasterVolume,
    bool IsMuted,
    string? HardwareInstanceId = null);

public sealed record AudioSessionInfo(
    string SessionId,
    string ApplicationKey,
    string DisplayName,
    string? ExecutablePath,
    int? ProcessId,
    bool IsSystemSounds,
    float Volume,
    bool IsMuted);

public sealed record MixerSnapshot(
    AudioEndpointInfo Endpoint,
    IReadOnlyList<AudioSessionInfo> Sessions);

public enum AudioDeviceDirection
{
    Output,
    Input
}

public enum AudioChangeKind
{
    DefaultDevice,
    MasterVolume,
    SessionCreated,
    SessionVolume,
    DeviceCollection,
    DefaultCaptureDevice,
    CaptureVolume
}

public sealed class AudioStateChangedEventArgs(
    AudioChangeKind kind,
    string? deviceId = null,
    string? applicationKey = null,
    float? volume = null,
    bool? isMuted = null,
    AudioDeviceDirection? deviceDirection = null) : EventArgs
{
    public AudioChangeKind Kind { get; } = kind;
    public string? DeviceId { get; } = deviceId;
    public string? ApplicationKey { get; } = applicationKey;
    public float? Volume { get; } = volume;
    public bool? IsMuted { get; } = isMuted;
    public AudioDeviceDirection? DeviceDirection { get; } = deviceDirection;
}

public interface IAudioService : IDisposable
{
    event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    IReadOnlyList<AudioEndpointInfo> GetRenderDevices();
    IReadOnlyList<AudioEndpointInfo> GetCaptureDevices();
    string? GetDefaultRenderDeviceId();
    string? GetDefaultCaptureDeviceId();
    MixerSnapshot? GetDefaultMixerSnapshot();
    void SetMasterVolume(string deviceId, float volume, bool? muted = null);
    void SetApplicationVolume(string deviceId, string applicationKey, float volume, bool? muted = null);
    void SetDefaultDevice(string deviceId, AudioDeviceDirection direction);
    void StartMonitoring();
}
