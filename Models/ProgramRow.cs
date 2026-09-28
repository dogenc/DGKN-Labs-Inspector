using System.ComponentModel;
using System.Windows.Media;
using DGKNInspector.Services;

namespace DGKNInspector.Models;

public sealed class ProgramRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Publisher { get; init; }   // aus der Registry
    public required string InstallDate { get; init; }
    public required string SizeText { get; init; }
    public required string Location { get; init; }
    public required string ExePath { get; init; }
    public required string DisplayIcon { get; init; }
    public required string Scope { get; init; }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { if (_icon != value) { _icon = value; Raise(nameof(Icon)); } }
    }

    // ---- Aus der Signatur ermittelt ----
    private string _company = "";
    public string Company   // Firma laut Zertifikat (Fallback: Publisher)
    {
        get => string.IsNullOrEmpty(_company) ? Publisher : _company;
        set { if (_company != value) { _company = value; Raise(nameof(Company)); } }
    }

    private string _country = "…";
    public string Country
    {
        get => _country;
        set { if (_country != value) { _country = value; Raise(nameof(Country)); } }
    }

    private string _countryCode = "";
    public string CountryCode
    {
        get => _countryCode;
        set { if (_countryCode != value) { _countryCode = value; Raise(nameof(CountryCode)); } }
    }

    private ImageSource? _flag;
    public ImageSource? FlagImage
    {
        get => _flag;
        set { if (_flag != value) { _flag = value; Raise(nameof(FlagImage)); } }
    }

    private string _sigText = "…";
    public string SignatureText
    {
        get => _sigText;
        set { if (_sigText != value) { _sigText = value; Raise(nameof(SignatureText)); } }
    }

    // Farbe des Signatur-Badges: grün gültig, rot ungültig, grau unsigniert.
    private Brush _sigBrush = Brushes.Gray;
    public Brush SignatureBrush
    {
        get => _sigBrush;
        set { if (_sigBrush != value) { _sigBrush = value; Raise(nameof(SignatureBrush)); } }
    }

    private Brush _sigBg = Brushes.Transparent;
    public Brush SignatureBg
    {
        get => _sigBg;
        set { if (_sigBg != value) { _sigBg = value; Raise(nameof(SignatureBg)); } }
    }

    public SignatureInspector.SignState State { get; set; } = SignatureInspector.SignState.Unknown;
}
