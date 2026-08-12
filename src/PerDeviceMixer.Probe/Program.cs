using PerDeviceMixer.Audio;
using PerDeviceMixer.Core;

var stressVolumeIndex = Array.FindIndex(
    args,
    argument => string.Equals(argument, "--external-stress-master", StringComparison.OrdinalIgnoreCase));
if (stressVolumeIndex >= 0)
{
    var cycles = stressVolumeIndex + 1 < args.Length &&
                 int.TryParse(args[stressVolumeIndex + 1], out var parsedCycles)
        ? Math.Clamp(parsedCycles, 1, 500)
        : 50;
    using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
    using var device = enumerator.GetDefaultAudioEndpoint(
        NAudio.CoreAudioApi.DataFlow.Render,
        NAudio.CoreAudioApi.Role.Multimedia);
    var endpoint = device.AudioEndpointVolume;
    var original = endpoint.MasterVolumeLevelScalar;
    var alternate = Math.Clamp(original + (original <= 0.98f ? 0.01f : -0.01f), 0f, 1f);
    endpoint.NotificationGuid = Guid.NewGuid();
    try
    {
        for (var index = 0; index < cycles; index++)
        {
            endpoint.MasterVolumeLevelScalar = index % 2 == 0 ? alternate : original;
            Thread.Sleep(15);
        }
    }
    finally
    {
        endpoint.MasterVolumeLevelScalar = original;
    }

    Console.WriteLine($"Completed {cycles} external master-volume changes; restored {original:P0}.");
    return;
}

var externalVolumeIndex = Array.FindIndex(
    args,
    argument => string.Equals(argument, "--external-set-master", StringComparison.OrdinalIgnoreCase));
if (externalVolumeIndex >= 0)
{
    if (externalVolumeIndex + 1 >= args.Length ||
        !float.TryParse(args[externalVolumeIndex + 1], out var percent))
    {
        Console.Error.WriteLine("Usage: --external-set-master <0-100>");
        Environment.ExitCode = 2;
        return;
    }

    using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
    using var device = enumerator.GetDefaultAudioEndpoint(
        NAudio.CoreAudioApi.DataFlow.Render,
        NAudio.CoreAudioApi.Role.Multimedia);
    device.AudioEndpointVolume.NotificationGuid = Guid.NewGuid();
    device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent / 100f, 0f, 1f);
    Console.WriteLine($"External master volume set to {Math.Clamp(percent, 0f, 100f):0}%.");
    return;
}

if (args.Contains("--capture-profile", StringComparer.OrdinalIgnoreCase))
{
    using var engine = new MixerEngine(new CoreAudioService(), new JsonProfileStore());
    await engine.InitializeAsync();
    await engine.FlushAsync();
    Console.WriteLine($"Profile captured: {engine.ProfilePath}");
    return;
}

using var audio = new CoreAudioService();
var devices = audio.GetRenderDevices();

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("PerDeviceMixer Core Audio Probe");
Console.WriteLine($"Render endpoints: {devices.Count}");

foreach (var device in devices)
{
    var marker = device.IsDefault ? "*" : " ";
    Console.WriteLine($"{marker} {device.Name}");
    Console.WriteLine($"  ID: {device.Id}");
    Console.WriteLine($"  Master: {device.MasterVolume:P0}  Muted: {device.IsMuted}");
}

var snapshot = audio.GetDefaultMixerSnapshot();
if (snapshot is null)
{
    Console.WriteLine("No default render endpoint.");
    return;
}

Console.WriteLine();
Console.WriteLine($"Default mixer: {snapshot.Endpoint.Name}");
foreach (var session in snapshot.Sessions
             .GroupBy(item => item.ApplicationKey, StringComparer.OrdinalIgnoreCase)
             .Select(group => group.First())
             .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
{
    Console.WriteLine($"- {session.DisplayName}: {session.Volume:P0}  Muted: {session.IsMuted}");
    Console.WriteLine($"  Key: {session.ApplicationKey}");
}

if (!args.Contains("--watch", StringComparer.OrdinalIgnoreCase)) return;

using var stopped = new ManualResetEventSlim();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.Set();
};

audio.StateChanged += (_, eventArgs) =>
    Console.WriteLine($"[{DateTime.Now:T}] {eventArgs.Kind}: {eventArgs.DeviceId} {eventArgs.ApplicationKey}");
audio.StartMonitoring();
Console.WriteLine();
Console.WriteLine("Watching Core Audio events. Press Ctrl+C to stop.");
stopped.Wait();
