using System.Diagnostics;
using System.Windows;
using DGKNInspector.Services;
using DGKNInspector.Views;

namespace DGKNInspector;

public partial class MainWindow : Window
{
    private IInspectorView? _current;

    public MainWindow()
    {
        InitializeComponent();

        ProgramsView.HeaderChanged += (s, _) => { if (s == _current) UpdateHeader(); };
        NetworkView.HeaderChanged  += (s, _) => { if (s == _current) UpdateHeader(); };

        Loaded += (_, _) =>
        {
            UpdateAdminLabel();
            ShowView(ProgramsView);
        };
    }

    // ===================== Tabs =====================

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // beim Start übernimmt Loaded
        ShowView(sender == TabNetwork ? NetworkView : ProgramsView);
    }

    private void ShowView(IInspectorView view)
    {
        if (ReferenceEquals(view, _current)) return;
        _current?.Deactivate();

        bool net = ReferenceEquals(view, NetworkView);
        ProgramsView.Visibility = net ? Visibility.Collapsed : Visibility.Visible;
        NetworkView.Visibility  = net ? Visibility.Visible : Visibility.Collapsed;
        SubtitleText.Text = net
            ? "DGKN@Labs · Aktive Netzwerkverbindungen · Prozess & Signatur"
            : "DGKN@Labs · Installierte Programme · Herkunft & Signatur";

        _current = view;
        UpdateHeader();
        view.Activate();
    }

    private void UpdateHeader()
    {
        if (_current == null) return;
        CountText.Text = _current.Count > 0 || _current.Progress.Length > 0 ? _current.Count.ToString() : "—";
        CountLabelText.Text = _current.CountLabel;
        ProgressText.Text = _current.Progress;
    }

    // ===================== Administrator =====================

    private void Admin_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.IsAdmin()) return;
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch { /* UAC abgebrochen */ }
    }

    private void UpdateAdminLabel()
    {
        if (!AppServices.IsAdmin()) return;
        AdminButton.Content = "🛡 Admin aktiv";
        AdminButton.IsEnabled = false;
        Title = "DGKN@Labs inspector — Administrator";
    }
}
