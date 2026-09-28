using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DGKNInspector.Services;

/// <summary>
/// Löst eine PID in Prozessname, vollständigen Pfad und Ordner auf.
/// Nutzt QueryFullProcessImageName (funktioniert zuverlässiger als
/// Process.MainModule) und cached die Ergebnisse pro PID.
/// </summary>
public sealed class ProcessResolver
{
    public sealed record ProcInfo(int Pid, string Name, string Path, string Folder);

    private readonly Dictionary<int, ProcInfo> _cache = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags,
        StringBuilder buf, ref int size);

    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public ProcInfo Resolve(int pid)
    {
        if (_cache.TryGetValue(pid, out var cached))
            return cached;

        ProcInfo info = BuildInfo(pid);
        _cache[pid] = info;
        return info;
    }

    /// <summary>Cache leeren (z. B. beim manuellen Aktualisieren), damit
    /// beendete PIDs nicht ewig hängen bleiben.</summary>
    public void Clear() => _cache.Clear();

    private ProcInfo BuildInfo(int pid)
    {
        if (pid == 0)
            return new ProcInfo(0, "System Idle", "", "");
        if (pid == 4)
            return new ProcInfo(4, "System", "", "");

        string name = $"PID {pid}";
        try { name = Process.GetProcessById(pid).ProcessName; } catch { }

        string path = "";
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageNameW(h, 0, sb, ref size))
                    path = sb.ToString();
            }
            finally { CloseHandle(h); }
        }

        string folder = "";
        if (!string.IsNullOrEmpty(path))
        {
            try { folder = Path.GetDirectoryName(path) ?? ""; } catch { }
            if (name.StartsWith("PID "))
            {
                try { name = Path.GetFileNameWithoutExtension(path); } catch { }
            }
        }

        return new ProcInfo(pid, name, path, folder);
    }
}
