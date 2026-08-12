using System.Text.Json;

namespace PerDeviceMixer.Core;

public interface IProfileStore
{
    string FilePath { get; }
    Task<MixerProfileDocument> LoadAsync(CancellationToken cancellationToken = default);
    void Save(MixerProfileDocument document);
    Task SaveAsync(MixerProfileDocument document, CancellationToken cancellationToken = default);
}

public sealed class JsonProfileStore(string? filePath = null) : IProfileStore
{
    public string FilePath { get; } = filePath ?? Path.Combine(
        PerDeviceMixerDataPaths.GetDataDirectory(),
        "profiles.json");

    public async Task<MixerProfileDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new MixerProfileDocument();
        }

        try
        {
            await using var stream = new FileStream(
                FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync(
                               stream,
                               ProfileJsonContext.Default.MixerProfileDocument,
                               cancellationToken).ConfigureAwait(false)
                           ?? new MixerProfileDocument();
            Normalize(document);
            return document;
        }
        catch (JsonException)
        {
            var corruptPath = FilePath + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(FilePath, corruptPath, overwrite: false);
            return new MixerProfileDocument();
        }
    }

    public async Task SaveAsync(
        MixerProfileDocument document,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("Profile path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = FilePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    ProfileJsonContext.Default.MixerProfileDocument,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Save(MixerProfileDocument document)
    {
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("Profile path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = FilePath + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(
                    stream,
                    document,
                    ProfileJsonContext.Default.MixerProfileDocument);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void Normalize(MixerProfileDocument document)
    {
        document.SchemaVersion = MixerProfileDocument.CurrentSchemaVersion;
        document.Settings ??= new MixerSettings();
        document.Devices ??= new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);

        if (document.Settings.UpdateCheckIntervalHours is not (6 or 24 or 72 or 168))
        {
            document.Settings.UpdateCheckIntervalHours = 24;
        }

        document.Settings.SaveDebounceMilliseconds = Math.Clamp(
            document.Settings.SaveDebounceMilliseconds,
            100,
            5000);
    }
}
