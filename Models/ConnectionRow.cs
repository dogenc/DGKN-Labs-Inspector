using System.ComponentModel;
using System.Net;
using System.Windows.Media;
using DGKNInspector.Services;

namespace DGKNInspector.Models;

public sealed class ConnectionRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    public required string Protocol { get; init; }
    public required string ProcessName { get; init; }
    public required int Pid { get; init; }
    public required string ProcessPath { get; init; }
    public required string ProcessFolder { get; init; }
    public required string Local { get; init; }
    public required string Remote { get; init; }

    private string _state = "";
    public required string State
    {
        get => _state;
        set { if (_state != value) { _state = value; Raise(nameof(State)); } }
    }

    public IPAddress? RemoteIp { get; init; }

    // Für Gruppierung/Filter: eindeutiger Schlüssel einer Verbindung
    public required string Key { get; init; }

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

    private string _isp = "";
    public string Isp
    {
        get => _isp;
        set { if (_isp != value) { _isp = value; Raise(nameof(Isp)); } }
    }

    private ImageSource? _flagImage;
    public ImageSource? FlagImage
    {
        get => _flagImage;
        set { if (_flagImage != value) { _flagImage = value; Raise(nameof(FlagImage)); } }
    }

    // ---- Signatur der Prozess-EXE ----
    private string _sigText = "…";
    public string SignatureText
    {
        get => _sigText;
        set { if (_sigText != value) { _sigText = value; Raise(nameof(SignatureText)); } }
    }

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

    public SignatureInspector.SignState SignState { get; private set; } = SignatureInspector.SignState.Unknown;
    public bool SignatureChecked { get; private set; }

    public void ApplySignature(SignatureInspector.SigInfo sig)
    {
        SignState = sig.State;
        SignatureChecked = true;
        (SignatureText, SignatureBrush, SignatureBg) = SignatureBadge.Style(sig.State);
    }

    public void ApplyGeo(GeoIpService.GeoInfo g)
    {
        Country = g.Country;
        CountryCode = g.CountryCode;
        Isp = g.Isp;
    }
}
