using System.Collections.Concurrent;
using System.Windows.Media;

namespace DGKNInspector.Services;

/// <summary>
/// Gemeinsame Darstellung und Cache der Signaturprüfung für beide Ansichten.
/// Programme und Netzwerk-Prozesse zeigen oft dieselben EXE-Dateien – jede
/// Datei wird daher nur einmal geprüft.
/// </summary>
public static class SignatureBadge
{
    public static readonly Brush GreenFg = Frozen("#FF1B9E4B");
    public static readonly Brush GreenBg = Frozen("#FFE8F7EE");
    public static readonly Brush RedFg   = Frozen("#FFD93025");
    public static readonly Brush RedBg   = Frozen("#FFFDECEA");
    public static readonly Brush GrayFg  = Frozen("#FF86868B");
    public static readonly Brush GrayBg  = Frozen("#FFF0F0F3");

    private static readonly ConcurrentDictionary<string, SignatureInspector.SigInfo> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    /// <summary>Prüft eine Datei (gecacht). Darf im Hintergrund aufgerufen werden.</summary>
    public static SignatureInspector.SigInfo Inspect(string? path)
    {
        if (string.IsNullOrEmpty(path)) return SignatureInspector.None;
        return Cache.GetOrAdd(path, p => SignatureInspector.Inspect(p));
    }

    /// <summary>Löscht den Cache, z. B. bei „Neu scannen“.</summary>
    public static void Clear() => Cache.Clear();

    public static (string Text, Brush Fg, Brush Bg) Style(SignatureInspector.SignState s) => s switch
    {
        SignatureInspector.SignState.Valid    => ("✓ Gültig signiert", GreenFg, GreenBg),
        SignatureInspector.SignState.Invalid  => ("⚠ Signatur ungültig", RedFg, RedBg),
        SignatureInspector.SignState.Unsigned => ("Unsigniert", GrayFg, GrayBg),
        _                                     => ("—", GrayFg, Brushes.Transparent)
    };

    public static string Plain(SignatureInspector.SignState s) => s switch
    {
        SignatureInspector.SignState.Valid    => "Gültig signiert",
        SignatureInspector.SignState.Invalid  => "Signatur ungültig",
        SignatureInspector.SignState.Unsigned => "Unsigniert",
        _                                     => "unbekannt"
    };

    public static bool IsSuspicious(SignatureInspector.SignState s)
        => s is SignatureInspector.SignState.Unsigned or SignatureInspector.SignState.Invalid;
}
