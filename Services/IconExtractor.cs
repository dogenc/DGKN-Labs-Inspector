using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DGKNInspector.Services;

/// <summary>Zieht das Programm-Icon aus einer EXE oder .ico-Datei und cached es.</summary>
public sealed class IconExtractor
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index,
        IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public ImageSource? Get(string? exePath, string displayIcon)
    {
        string key = !string.IsNullOrEmpty(exePath) ? exePath : displayIcon;
        if (string.IsNullOrEmpty(key)) return null;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        ImageSource? img = Build(exePath, displayIcon);
        _cache[key] = img;
        return img;
    }

    private static ImageSource? Build(string? exePath, string displayIcon)
    {
        // Direkte .ico-Datei?
        try
        {
            string di = displayIcon;
            int comma = di.LastIndexOf(',');
            int idx = 0;
            if (comma > 1 && comma > di.LastIndexOf('\\'))
            {
                int.TryParse(di[(comma + 1)..].Trim(), out idx);
                di = di[..comma];
            }
            di = di.Trim().Trim('"');

            if (di.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) && File.Exists(di))
            {
                var frame = BitmapFrame.Create(new Uri(di), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }

            string src = File.Exists(di) && di.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? di
                : (exePath ?? "");
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) return null;

            var large = new IntPtr[1];
            uint n = ExtractIconEx(src, idx, large, null, 1);
            if (n == 0 || large[0] == IntPtr.Zero)
            {
                n = ExtractIconEx(src, 0, large, null, 1);
                if (n == 0 || large[0] == IntPtr.Zero) return null;
            }
            try
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(
                    large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            finally { DestroyIcon(large[0]); }
        }
        catch { return null; }
    }
}
