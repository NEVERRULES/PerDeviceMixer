using PerDeviceMixer.Core;

namespace PerDeviceMixer.Core.Tests;

public sealed class JsonProfileStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsDeviceAndApplicationProfiles()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonProfileStore(Path.Combine(directory.Path, "profiles.json"));
        var document = CreateDocument();

        await store.SaveAsync(document);
        var loaded = await store.LoadAsync();

        var device = Assert.Single(loaded.Devices).Value;
        Assert.Equal("Speakers", device.Name);
        Assert.Equal(0.26f, device.MasterVolume);
        var application = Assert.Single(device.Applications).Value;
        Assert.Equal("Chrome", application.DisplayName);
        Assert.Equal(0.30f, application.Volume);
        Assert.False(application.Muted);
    }

    [Fact]
    public async Task SaveAsyncReplacesPreviousFileWithoutLeavingTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        var store = new JsonProfileStore(path);
        var document = CreateDocument();

        await store.SaveAsync(document);
        document.Devices["device-a"].MasterVolume = 0.8f;
        await store.SaveAsync(document);

        var loaded = await store.LoadAsync();
        Assert.Equal(0.8f, loaded.Devices["device-a"].MasterVolume);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task LoadAsyncQuarantinesInvalidJsonAndReturnsDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        await File.WriteAllTextAsync(path, "{ definitely not json }");
        var store = new JsonProfileStore(path);

        var loaded = await store.LoadAsync();

        Assert.Empty(loaded.Devices);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(directory.Path, "profiles.json.corrupt-*"));
    }

    [Fact]
    public async Task LoadAsyncMigratesVersionOneSettingsToSchemaVersionTwo()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profiles.json");
        await File.WriteAllTextAsync(path, """
            {
              "SchemaVersion": 1,
              "Settings": {
                "AutoLearn": true,
                "UpdateCheckIntervalHours": 999
              },
              "Devices": {}
            }
            """);
        var store = new JsonProfileStore(path);

        var loaded = await store.LoadAsync();

        Assert.Equal(MixerProfileDocument.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.True(loaded.Settings.AutomaticUpdateChecks);
        Assert.Equal(24, loaded.Settings.UpdateCheckIntervalHours);
        Assert.Null(loaded.Settings.LastSuccessfulUpdateCheckUtc);
        Assert.Null(loaded.Settings.LastUpdateAttemptUtc);
    }

    private static MixerProfileDocument CreateDocument()
    {
        var document = new MixerProfileDocument();
        document.Devices["device-a"] = new DeviceProfile
        {
            DeviceId = "device-a",
            Name = "Speakers",
            MasterVolume = 0.26f,
            MasterMuted = false,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            Applications =
            {
                ["path:c:\\chrome.exe"] = new ApplicationVolumeProfile
                {
                    ApplicationKey = "path:c:\\chrome.exe",
                    DisplayName = "Chrome",
                    ExecutablePath = "C:\\chrome.exe",
                    Volume = 0.30f,
                    Muted = false,
                    LastUpdatedUtc = DateTimeOffset.UtcNow
                }
            }
        };
        return document;
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
