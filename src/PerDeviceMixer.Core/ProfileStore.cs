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
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerDeviceMixer",
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

            return await JsonSerializer.DeserializeAsync(
                       stream,
                       ProfileJsonContext.Default.MixerProfileDocument,
                       cancellationToken).ConfigureAwait(false)
                   ?? new MixerProfileDocument();
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
}
