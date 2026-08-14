namespace PerDeviceMixer.Core;

public sealed record AudioDiagnosticEntry(
    DateTimeOffset TimestampUtc,
    AudioChangeKind Kind,
    string Stage,
    string? DeviceToken,
    string? ApplicationToken,
    string? ErrorType);

public interface IAudioDiagnosticSink
{
    void Write(AudioDiagnosticEntry entry);
}

/// <summary>
/// Writes opt-in, privacy-filtered Core Audio event diagnostics to the local application data folder.
/// </summary>
public sealed class LocalAudioDiagnosticLog(string? filePath = null) : IAudioDiagnosticSink
{
    private const long MaximumFileBytes = 256 * 1024;
    private readonly object _writeLock = new();

    public string FilePath { get; } = filePath ?? Path.Combine(
        PerDeviceMixerDataPaths.GetDataDirectory(),
        "logs",
        "audio-events.log");

    public void Write(AudioDiagnosticEntry entry)
    {
        try
        {
            lock (_writeLock)
            {
                var directory = Path.GetDirectoryName(FilePath)
                    ?? throw new InvalidOperationException("Diagnostic log path has no parent directory.");
                Directory.CreateDirectory(directory);
                RotateIfNeeded();
                File.AppendAllText(
                    FilePath,
                    $"{entry.TimestampUtc:O}\t{entry.Kind}\t{entry.Stage}\tdevice={entry.DeviceToken ?? "-"}\tapplication={entry.ApplicationToken ?? "-"}\terror={entry.ErrorType ?? "-"}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Diagnostics must never interfere with audio control or profile persistence.
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < MaximumFileBytes) return;
        File.Move(FilePath, FilePath + ".previous", overwrite: true);
    }
}
