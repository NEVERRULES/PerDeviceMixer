using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PerDeviceMixer.App;

internal sealed class ApplicationIconProvider : IDisposable
{
    private readonly ConcurrentDictionary<string, BitmapSource> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public BitmapSource GetIcon(
        string applicationKey,
        string? executablePath,
        bool isSystemSounds) =>
        _cache.GetOrAdd(applicationKey, _ => CreateIcon(executablePath, isSystemSounds));

    private static BitmapSource CreateIcon(string? executablePath, bool isSystemSounds)
    {
        Icon? extractedIcon = null;
        try
        {
            var iconPath = executablePath;
            if (!string.IsNullOrWhiteSpace(iconPath) && System.IO.File.Exists(iconPath))
            {
                extractedIcon = Icon.ExtractAssociatedIcon(iconPath);
            }

            extractedIcon ??= (Icon)(isSystemSounds
                ? SystemIcons.Information.Clone()
                : SystemIcons.Application.Clone());

            var source = Imaging.CreateBitmapSourceFromHIcon(
                extractedIcon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        catch
        {
            using var fallback = (Icon)SystemIcons.Application.Clone();
            var source = Imaging.CreateBitmapSourceFromHIcon(
                fallback.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        finally
        {
            extractedIcon?.Dispose();
        }
    }

    public void Dispose() => _cache.Clear();
}
