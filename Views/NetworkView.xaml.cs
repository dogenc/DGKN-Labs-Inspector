using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using DGKNInspector.Models;
using DGKNInspector.Services;

namespace DGKNInspector.Views;

/// <summary>Tab „Netzwerk“: aktive TCP/UDP-Verbindungen mit Prozess, Signatur und Herkunft.</summary>
public partial class NetworkView : UserControl, IInspectorView
{
    private readonly ProcessResolver _procs = new();
    private readonly GeoIpService _geo = new();

    private readonly ObservableCollection<ConnectionRow> _rows = new();
    private readonly ICollectionView _view;

    private readonly DispatcherTimer _timer;
    private bool _busy;
    private bool _active;

    public event EventHandler? HeaderChanged;
    public int Count { get; private set; }
    public string CountLabel => "Verbindungen";
    public string Progress { get; private set; } = "";

    public NetworkView()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterPredicate;
        Table.ItemsSource = _view;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Tab sichtbar: sofort aktualisieren, Auto-Aktualisierung starten.</summary>
    public async void Activate()
    {
        _active = true;
        await RefreshAsync();
        if (_active && AutoRefresh.IsChecked == true) _timer.Start();
    }

    /// <summary>Tab verlassen: keine Hintergrund-Abfragen, solange er nicht sichtbar ist.</summary>
    public void Deactivate()
    {
        _active = false;
        _timer.Stop();
    }

    // ===================== Aktualisieren =====================

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _procs.Clear(); // beendete PIDs nicht ewig halten

            var built = await Task.Run(() =>
            {
                var conns = IpHelper.GetAllConnections();
                var result = new List<ConnectionRow>(conns.Count);
                foreach (var c in conns)
                {
                    var p = _procs.Resolve(c.Pid);
                    string proto = c.Protocol switch
                    {
                        IpHelper.Proto.TcpV4 => "TCP",
                        IpHelper.Proto.TcpV6 => "TCP6",
                        IpHelper.Proto.UdpV4 => "UDP",
                        IpHelper.Proto.UdpV6 => "UDP6",
                        _ => "?"
                    };

                    string local = FormatEndpoint(c.LocalAddress, c.LocalPort);
                    string remote = c.RemoteAddress is null
                        ? "*"
                        : FormatEndpoint(c.RemoteAddress, c.RemotePort);

                    result.Add(new ConnectionRow
                    {
                        Protocol = proto,
                        ProcessName = p.Name,
                        Pid = c.Pid,
                        ProcessPath = p.Path,
                        ProcessFolder = string.IsNullOrEmpty(p.Folder) ? "—" : p.Folder,
                        Local = local,
                        Remote = remote,
                        State = c.State,
                        RemoteIp = c.RemoteAddress,
                        Key = $"{proto}|{local}|{remote}|{c.Pid}"
                    });
                }
                return result;
            });

            MergeInto(built);
            _view.Refresh();
            UpdateCount();
            Progress = "aktualisiert " + DateTime.Now.ToString("HH:mm:ss");
            HeaderChanged?.Invoke(this, EventArgs.Empty);

            if (Count == 0)
            {
                EmptyText.Text = "Keine Verbindungen für diesen Filter.";
                EmptyText.Visibility = Visibility.Visible;
            }
            else EmptyText.Visibility = Visibility.Collapsed;

            await ResolveSignaturesAsync();

            if (GeoEnabled.IsChecked == true)
                await ResolveGeoAsync();
            else
                foreach (var r in _rows) { if (r.Country == "…") r.Country = "—"; }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Fügt die frisch gelesenen Verbindungen in die bestehende Liste ein,
    /// ohne sie komplett neu aufzubauen (kein Scroll-Sprung, kein Flackern).</summary>
    private void MergeInto(List<ConnectionRow> fresh)
    {
        var freshByKey = new Dictionary<string, ConnectionRow>(fresh.Count);
        foreach (var r in fresh) freshByKey[r.Key] = r;

        var existing = new Dictionary<string, ConnectionRow>(_rows.Count);
        foreach (var r in _rows) existing[r.Key] = r;

        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!freshByKey.ContainsKey(_rows[i].Key))
                _rows.RemoveAt(i);

        foreach (var r in fresh)
        {
            if (existing.TryGetValue(r.Key, out var old))
            {
                old.State = r.State; // einziges veränderliches Feld
                continue;
            }

            var cached = r.RemoteIp is null ? null : _geo.FromCache(r.RemoteIp.ToString());
            if (cached != null)
            {
                r.ApplyGeo(cached);
                r.FlagImage = AppServices.Flags.FromCache(cached.CountryCode);
            }
            else if (r.RemoteIp != null && GeoIpService.IsPrivateOrLocal(r.RemoteIp))
                r.Country = IPAddress.IsLoopback(r.RemoteIp) ? "Lokal" : "Privates Netz";
            else if (r.RemoteIp is null)
                r.Country = "—";
            _rows.Add(r);
        }
    }

    /// <summary>Prüft die Signatur jeder Prozess-EXE einmal (gemeinsamer Cache
    /// mit dem Programme-Tab) und trägt sie in alle zugehörigen Zeilen ein.</summary>
    private async Task ResolveSignaturesAsync()
    {
        var pending = _rows.Where(r => !r.SignatureChecked).ToList();
        if (pending.Count == 0) return;

        var paths = pending.Select(r => r.ProcessPath)
                           .Where(p => !string.IsNullOrEmpty(p))
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .ToList();

        var results = await Task.Run(() =>
            paths.ToDictionary(p => p, SignatureBadge.Inspect, StringComparer.OrdinalIgnoreCase));

        foreach (var r in pending)
        {
            r.ApplySignature(!string.IsNullOrEmpty(r.ProcessPath) && results.TryGetValue(r.ProcessPath, out var s)
                ? s
                : SignatureInspector.None);
        }

        if (ProblemsOnly.IsChecked == true)
        {
            _view.Refresh();
            UpdateCount();
        }
    }

    private async Task ResolveGeoAsync()
    {
        var ips = new List<IPAddress>();
        foreach (var r in _rows)
            if (r.RemoteIp != null && !GeoIpService.IsPrivateOrLocal(r.RemoteIp))
                ips.Add(r.RemoteIp);

        if (ips.Count == 0) return;

        StatusText.Text = "Länder werden abgefragt…";
        await _geo.ResolveManyAsync(ips);

        foreach (var r in _rows)
        {
            if (r.RemoteIp == null) continue;
            var g = _geo.FromCache(r.RemoteIp.ToString());
            if (g != null) r.ApplyGeo(g);
        }

        foreach (var r in _rows)
        {
            if (r.FlagImage != null || r.CountryCode.Length != 2) continue;
            var img = await AppServices.Flags.GetAsync(r.CountryCode);
            if (img != null) r.FlagImage = img;
        }
        StatusText.Text = "Doppelklick auf eine Zeile öffnet den Programmordner.";
    }

    private static string FormatEndpoint(IPAddress ip, int port)
    {
        string a = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{ip}]" : ip.ToString();
        return port == 0 ? a : $"{a}:{port}";
    }

    // ===================== Filter =====================

    private bool FilterPredicate(object o)
    {
        if (o is not ConnectionRow r) return false;

        if (ExternalOnly.IsChecked == true &&
            (r.RemoteIp == null || GeoIpService.IsPrivateOrLocal(r.RemoteIp)))
            return false;

        if (HideListening.IsChecked == true &&
            (r.State.Contains("Lausch", StringComparison.OrdinalIgnoreCase) || r.State == "—"))
            return false;

        if (ProblemsOnly.IsChecked == true && !SignatureBadge.IsSuspicious(r.SignState))
            return false;

        string q = SearchBox.Text.Trim();
        if (q.Length > 0)
        {
            bool hit =
                r.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.ProcessPath.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Local.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Remote.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Country.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Isp.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Protocol.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Pid.ToString().Contains(q);
            if (!hit) return false;
        }
        return true;
    }

    // ===================== UI-Ereignisse =====================

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateCount();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _view.Refresh();
        UpdateCount();
    }

    private async void Geo_Toggled(object sender, RoutedEventArgs e)
    {
        bool on = GeoEnabled.IsChecked == true;
        GeoHint.Text = on
            ? "Länderdaten: ip-api.com · nur öffentliche IPs · lokal gecacht"
            : "Länderabfrage aus · keine Daten verlassen den PC";
        if (on) await ResolveGeoAsync();
    }

    private void Auto_Toggled(object sender, RoutedEventArgs e)
    {
        if (AutoRefresh.IsChecked == true && _active) _timer.Start();
        else _timer.Stop();
    }

    private void UpdateCount()
    {
        Count = _view.Cast<object>().Count();
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Table_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Table.SelectedItem is not ConnectionRow r) return;
        if (string.IsNullOrEmpty(r.ProcessFolder) || r.ProcessFolder == "—") return;
        try
        {
            if (!string.IsNullOrEmpty(r.ProcessPath) && File.Exists(r.ProcessPath))
                Process.Start("explorer.exe", $"/select,\"{r.ProcessPath}\"");
            else if (Directory.Exists(r.ProcessFolder))
                Process.Start("explorer.exe", $"\"{r.ProcessFolder}\"");
        }
        catch { /* Ordner evtl. nicht zugänglich */ }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Verbindungen als CSV speichern",
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"netzwerk_{DateTime.Now:yyyy-MM-dd_HH-mm}.csv"
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("sep=;");
            sb.AppendLine("Prozess;PID;Signatur;Protokoll;Lokal;Remote;Land;Ländercode;Provider;Status;Pfad");

            foreach (var o in _view)
            {
                if (o is not ConnectionRow r) continue;
                sb.AppendLine(string.Join(";",
                    AppServices.Csv(r.ProcessName), r.Pid, AppServices.Csv(SignatureBadge.Plain(r.SignState)),
                    AppServices.Csv(r.Protocol), AppServices.Csv(r.Local), AppServices.Csv(r.Remote),
                    AppServices.Csv(r.Country), AppServices.Csv(r.CountryCode), AppServices.Csv(r.Isp),
                    AppServices.Csv(r.State), AppServices.Csv(r.ProcessPath)));
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
            StatusText.Text = $"Exportiert: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Export fehlgeschlagen:\n" + ex.Message, "DGKN@Labs inspector",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
