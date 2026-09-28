using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DGKNInspector.Services;

/// <summary>
/// Liefert kleine Länderflaggen als Bild. Windows kann Flaggen-Emojis nicht
/// darstellen, daher werden 40px-PNGs von flagcdn.com geladen und dauerhaft
/// unter %LOCALAPPDATA%\DGKNInspector\flags zwischengespeichert. Nach dem ersten
/// Laden funktioniert es auch offline.
/// </summary>
public sealed class FlagService
{
    private readonly Dictionary<string, ImageSource> _mem = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<ImageSource?>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly string _cacheDir;

    public FlagService()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DGKNInspector", "flags");
        try { Directory.CreateDirectory(_cacheDir); } catch { }
    }

    public ImageSource? FromCache(string code)
        => _mem.TryGetValue(code, out var s) ? s : null;

    public Task<ImageSource?> GetAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 2)
            return Task.FromResult<ImageSource?>(null);

        if (_mem.TryGetValue(code, out var cached))
            return Task.FromResult<ImageSource?>(cached);

        if (_pending.TryGetValue(code, out var running))
            return running;

        var task = LoadAsync(code.ToLowerInvariant());
        _pending[code] = task;
        return task;
    }

    private async Task<ImageSource?> LoadAsync(string code)
    {
        try
        {
            string file = Path.Combine(_cacheDir, code + ".png");
            byte[]? bytes = null;

            if (File.Exists(file))
            {
                try { bytes = await File.ReadAllBytesAsync(file); } catch { }
            }

            if (bytes is null || bytes.Length == 0)
            {
                bytes = await _http.GetByteArrayAsync($"https://flagcdn.com/w40/{code}.png");
                try { await File.WriteAllBytesAsync(file, bytes); } catch { }
            }

            var img = MakeImage(bytes);
            if (img != null) _mem[code] = img;
            return img;
        }
        catch
        {
            return null;
        }
        finally
        {
            _pending.Remove(code);
        }
    }

    private static ImageSource? MakeImage(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze(); // threadübergreifend nutzbar
            return bmp;
        }
        catch { return null; }
    }
}
