using System.IO;
using Microsoft.Win32;

namespace DGKNInspector.Services;

/// <summary>
/// Liest installierte Programme aus den Uninstall-Registry-Schlüsseln
/// (Maschine 64-bit, Maschine 32-bit, aktueller Benutzer).
/// </summary>
public static class InstalledPrograms
{
    public sealed class Entry
    {
        public string Name = "";
        public string Version = "";
        public string Publisher = "";
        public string InstallDate = "";
        public string InstallLocation = "";
        public long SizeKb;
        public string DisplayIcon = "";
        public string UninstallString = "";
        public string Scope = "";
        public string ProbeExe = "";
    }

    public static List<Entry> Enumerate()
    {
        var list = new List<Entry>();
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, "Maschine", list);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, "Maschine (32-Bit)", list);
        Read(RegistryHive.CurrentUser,  RegistryView.Registry64, "Benutzer", list);

        // Duplikate (gleicher Name + Version) zusammenführen.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Entry>();
        foreach (var e in list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            string k = e.Name + "|" + e.Version;
            if (seen.Add(k)) result.Add(e);
        }
        return result;
    }

    private static void Read(RegistryHive hive, RegistryView view, string scope, List<Entry> outList)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) return;

            foreach (var sub in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var k = uninstall.OpenSubKey(sub);
                    if (k == null) continue;

                    string name = (k.GetValue("DisplayName") as string ?? "").Trim();
                    if (name.Length == 0) continue;

                    // Systemkomponenten und reine Updates ausblenden.
                    if ((k.GetValue("SystemComponent") as int? ?? 0) == 1) continue;
                    if (!string.IsNullOrEmpty(k.GetValue("ParentKeyName") as string)) continue;
                    string relType = k.GetValue("ReleaseType") as string ?? "";
                    if (relType.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                        relType.Equals("Security Update", StringComparison.OrdinalIgnoreCase) ||
                        relType.Equals("Hotfix", StringComparison.OrdinalIgnoreCase)) continue;

                    var e = new Entry
                    {
                        Name = name,
                        Version = k.GetValue("DisplayVersion") as string ?? "",
                        Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                        InstallDate = FormatDate(k.GetValue("InstallDate") as string ?? ""),
                        InstallLocation = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"'),
                        DisplayIcon = (k.GetValue("DisplayIcon") as string ?? "").Trim().Trim('"'),
                        UninstallString = k.GetValue("UninstallString") as string ?? "",
                        Scope = scope
                    };
                    e.SizeKb = k.GetValue("EstimatedSize") switch
                    {
                        int i => i,
                        long l => l,
                        _ => 0
                    };
                    e.ProbeExe = ResolveProbeExe(e);
                    outList.Add(e);
                }
                catch { /* einzelnen Eintrag überspringen */ }
            }
        }
        catch { /* Hive nicht lesbar */ }
    }

    /// <summary>Findet die aussagekräftigste EXE des Programms für Signatur & Icon.</summary>
    private static string ResolveProbeExe(Entry e)
    {
        string[] words = e.Name.Split(new[] { ' ', '-', '_', '(', ')' },
            StringSplitOptions.RemoveEmptyEntries);

        // 1) DisplayIcon zeigt oft direkt auf die Haupt-EXE ("pfad.exe,0").
        string di = e.DisplayIcon;
        int comma = di.LastIndexOf(',');
        if (comma > 1 && comma > di.LastIndexOf('\\')) di = di[..comma];
        di = di.Trim().Trim('"');
        if (di.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(di))
            return di;

        // Kandidaten-Ordner sammeln: InstallLocation + Ordner von DisplayIcon + Ordner der Uninstall-EXE.
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(e.InstallLocation)) roots.Add(e.InstallLocation);
        try { if (!string.IsNullOrEmpty(di)) { var d = Path.GetDirectoryName(di); if (!string.IsNullOrEmpty(d)) roots.Add(d); } } catch { }

        string uninstExe = ExeFromCommand(e.UninstallString);
        if (!string.IsNullOrEmpty(uninstExe))
        {
            try { var d = Path.GetDirectoryName(uninstExe); if (!string.IsNullOrEmpty(d)) roots.Add(d); } catch { }
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string found = BestExeIn(root, words);
            if (!string.IsNullOrEmpty(found)) return found;
        }

        // Uninstall-EXE selbst (kein Deinstaller/msiexec), als letzte Wahl.
        if (!string.IsNullOrEmpty(uninstExe) && File.Exists(uninstExe) && !LooksLikeUninstaller(uninstExe))
            return uninstExe;

        return "";
    }

    /// <summary>Beste EXE in einem Ordner (oben + eine Ebene tiefer, z. B. \bin, \app).</summary>
    private static string BestExeIn(string root, string[] words)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return "";
        try
        {
            var exes = new List<string>();
            exes.AddRange(Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly));
            if (exes.Count == 0)
            {
                foreach (var sub in Directory.EnumerateDirectories(root))
                {
                    try { exes.AddRange(Directory.EnumerateFiles(sub, "*.exe", SearchOption.TopDirectoryOnly)); }
                    catch { }
                    if (exes.Count > 400) break;
                }
            }
            if (exes.Count == 0) return "";

            // Deinstaller/Updater aussortieren.
            var useful = exes.Where(x => !LooksLikeUninstaller(x)).ToList();
            if (useful.Count == 0) useful = exes;

            // EXE, deren Name im Programmnamen vorkommt, bevorzugen; sonst die größte.
            var match = useful.FirstOrDefault(x =>
                words.Any(w => w.Length >= 3 &&
                    Path.GetFileNameWithoutExtension(x).Contains(w, StringComparison.OrdinalIgnoreCase)));
            if (match != null) return match;

            return useful.OrderByDescending(x => { try { return new FileInfo(x).Length; } catch { return 0L; } }).First();
        }
        catch { return ""; }
    }

    private static bool LooksLikeUninstaller(string path)
    {
        string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return n.Contains("uninst") || n.Contains("unins0") || n == "setup" ||
               n.Contains("update") || n.Contains("crashpad") || n.Contains("helper");
    }

    private static string ExeFromCommand(string cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return "";
        if (cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return "";
        string path;
        if (cmd.StartsWith("\""))
        {
            int q = cmd.IndexOf('"', 1);
            path = q > 0 ? cmd[1..q] : cmd.Trim('"');
        }
        else
        {
            int sp = cmd.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
            path = sp > 0 ? cmd[..(sp + 4)] : cmd.Split(' ')[0];
        }
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : "";
    }

    private static string FormatDate(string raw)
    {
        // Registry-Format meist yyyyMMdd
        if (raw.Length == 8 && raw.All(char.IsDigit))
        {
            if (DateTime.TryParseExact(raw, "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var d))
                return d.ToString("dd.MM.yyyy");
        }
        return raw;
    }
}
