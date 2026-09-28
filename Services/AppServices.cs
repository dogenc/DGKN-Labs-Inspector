namespace DGKNInspector.Services;

/// <summary>Von beiden Ansichten geteilte Dienste (ein Flaggen-Cache für alles).</summary>
public static class AppServices
{
    public static readonly FlagService Flags = new();

    public static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>CSV-Feld maskieren (Trennzeichen ;).</summary>
    public static string Csv(string? s)
    {
        s ??= "";
        if (s.Contains(';') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
