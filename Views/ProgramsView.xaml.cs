using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DGKNInspector.Models;
using DGKNInspector.Services;

namespace DGKNInspector.Views;

/// <summary>Tab „Programme“: installierte Software mit Signatur, Firma und Herkunft.</summary>
public partial class ProgramsView : UserControl, IInspectorView
{
    private readonly IconExtractor _icons = new();

    private readonly ObservableCollection<ProgramRow> _rows = new();
    private readonly ICollectionView _view;

    private CancellationTokenSource? _cts;
    private bool _started;

    public event EventHandler? HeaderChanged;
    public int Count { get; private set; }
    public string CountLabel => "Programme";
    public string Progress { get; private set; } = "";

    public ProgramsView()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = FilterPredicate;
        Table.ItemsSource = _view;
    }

    /// <summary>Wird aufgerufen, wenn der Tab sichtbar wird. Scannt beim ersten Mal.</summary>
    public async void Activate()
    {
        if (_started) return;
        _started = true;
        await ScanAsync();
    }

    public void Deactivate() { }

    // ===================== Scan =====================

    private async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        EmptyText.Visibility = Visibility.Visible;
        EmptyText.Text = "Wird gescannt…";
        SetProgress("lese Registry…");
        _rows.Clear();
        SignatureBadge.Clear();

        var entries = await Task.Run(InstalledPrograms.Enumerate, ct);
        if (ct.IsCancellationRequested) return;

        foreach (var e in entries)
        {
            _rows.Add(new ProgramRow
            {
                Name = e.Name,
                Version = string.IsNullOrEmpty(e.Version) ? "" : "v" + e.Version,
                Publisher = e.Publisher,
                InstallDate = string.IsNullOrEmpty(e.InstallDate) ? "—" : e.InstallDate,
                SizeText = FormatSize(e.SizeKb),
                Location = string.IsNullOrEmpty(e.InstallLocation)
                    ? (string.IsNullOrEmpty(e.ProbeExe) ? "—" : Path.GetDirectoryName(e.ProbeExe) ?? "—")
                    : e.InstallLocation,
                ExePath = e.ProbeExe,
                DisplayIcon = e.DisplayIcon,
                Scope = e.Scope
            });
        }

        _view.Refresh();
        UpdateCount();
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        await ResolveAllAsync(ct);
    }

    /// <summary>Prüft je Programm Signatur, Firma, Land, Flagge und Icon —
    /// im Hintergrund, mit fortlaufender Aktualisierung der Zeilen.</summary>
    private async Task ResolveAllAsync(CancellationToken ct)
    {
        int total = _rows.Count, done = 0;
        foreach (var row in _rows.ToList())
        {
            if (ct.IsCancellationRequested) return;

            var result = await Task.Run(() =>
            {
                var sig = SignatureBadge.Inspect(row.ExePath);
                var icon = _icons.Get(row.ExePath, row.DisplayIcon);
                return (sig, icon);
            }, ct);

            if (ct.IsCancellationRequested) return;

            row.State = result.sig.State;
            (row.SignatureText, row.SignatureBrush, row.SignatureBg) = SignatureBadge.Style(result.sig.State);
            if (result.icon != null) row.Icon = result.icon;

            if (!string.IsNullOrEmpty(result.sig.Company))
                row.Company = result.sig.Company;

            if (result.sig.CountryCode.Length == 2)
            {
                row.CountryCode = result.sig.CountryCode;
                row.Country = Countries.Name(result.sig.CountryCode);
                var f = await AppServices.Flags.GetAsync(result.sig.CountryCode);
                if (f != null) row.FlagImage = f;
            }
            else if (row.Country == "…")
            {
                row.Country = "—";
            }

            done++;
            SetProgress($"prüfe Signaturen… {done}/{total}");
        }
        // Filter „Nur unsigniert/Auffällige“ braucht die fertigen Ergebnisse.
        _view.Refresh();
        UpdateCount();
        SetProgress($"fertig · {DateTime.Now:HH:mm:ss}");
    }

    private static string FormatSize(long kb)
    {
        if (kb <= 0) return "—";
        double mb = kb / 1024.0;
        if (mb >= 1024) return $"{mb / 1024.0:0.0} GB";
        if (mb >= 1) return $"{mb:0} MB";
        return $"{kb} KB";
    }

    // ===================== Filter =====================

    private bool FilterPredicate(object o)
    {
        if (o is not ProgramRow r) return false;

        if (UnsignedOnly.IsChecked == true && r.State != SignatureInspector.SignState.Unsigned)
            return false;

        if (ProblemsOnly.IsChecked == true && !SignatureBadge.IsSuspicious(r.State))
            return false;

        string q = SearchBox.Text.Trim();
        if (q.Length > 0)
        {
            bool hit =
                r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Company.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Country.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.CountryCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Version.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Location.Contains(q, StringComparison.OrdinalIgnoreCase);
            if (!hit) return false;
        }
        return true;
    }

    // ===================== UI-Ereignisse =====================

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ScanAsync();

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

    private void UpdateCount()
    {
        Count = _view.Cast<object>().Count();
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetProgress(string text)
    {
        Progress = text;
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Table_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Table.SelectedItem is not ProgramRow r) return;
        try
        {
            if (!string.IsNullOrEmpty(r.ExePath) && File.Exists(r.ExePath))
                Process.Start("explorer.exe", $"/select,\"{r.ExePath}\"");
            else if (!string.IsNullOrEmpty(r.Location) && Directory.Exists(r.Location))
                Process.Start("explorer.exe", $"\"{r.Location}\"");
        }
        catch { }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Programme als CSV speichern",
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"programme_{DateTime.Now:yyyy-MM-dd_HH-mm}.csv"
        };
        if (dlg.ShowDialog(owner) != true) return;

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("sep=;");
            sb.AppendLine("Programm;Version;Firma;Land;Ländercode;Signatur;Installiert;Größe;Quelle;Speicherort;EXE");
            foreach (var o in _view)
            {
                if (o is not ProgramRow r) continue;
                sb.AppendLine(string.Join(";",
                    AppServices.Csv(r.Name), AppServices.Csv(r.Version), AppServices.Csv(r.Company),
                    AppServices.Csv(r.Country), AppServices.Csv(r.CountryCode),
                    AppServices.Csv(SignatureBadge.Plain(r.State)), AppServices.Csv(r.InstallDate),
                    AppServices.Csv(r.SizeText), AppServices.Csv(r.Scope),
                    AppServices.Csv(r.Location), AppServices.Csv(r.ExePath)));
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
