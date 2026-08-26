using System.Text.Json.Serialization;

namespace PerDeviceMixer.Core;

public sealed class MixerProfileDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public MixerSettings Settings { get; set; } = new();
    public Dictionary<string, DeviceProfile> Devices { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MixerSettings
{
    public bool AutoLearn { get; set; } = true;
    public bool AutoRestore { get; set; } = true;
    public bool RestoreNewSessions { get; set; } = true;
    public bool SaveMuteState { get; set; } = true;
    public bool ShowDeviceSwitchToast { get; set; } = true;
    public bool ShowSupportedHeadphoneBattery { get; set; } = true;
    public bool AudioDiagnosticsEnabled { get; set; }
    public int SaveDebounceMilliseconds { get; set; } = 500;
    public bool StartWithWindows { get; set; }
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.MinimizeToTray;
    public bool AutomaticUpdateChecks { get; set; } = true;
    public int UpdateCheckIntervalHours { get; set; } = 24;
    public DateTimeOffset? LastSuccessfulUpdateCheckUtc { get; set; }
    public DateTimeOffset? LastUpdateAttemptUtc { get; set; }
}

public enum CloseBehavior
{
    Exit,
    MinimizeToTray
}

public sealed class DeviceProfile
{
    public required string DeviceId { get; set; }
    public required string Name { get; set; }
    public float MasterVolume { get; set; }
    public bool MasterMuted { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }
    public Dictionary<string, ApplicationVolumeProfile> Applications { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ApplicationVolumeProfile
{
    public required string ApplicationKey { get; set; }
    public required string DisplayName { get; set; }
    public string? ExecutablePath { get; set; }
    public float Volume { get; set; }
    public bool Muted { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }
}

[JsonSerializable(typeof(MixerProfileDocument))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
