namespace DGKNInspector.Services;

/// <summary>ISO-3166-Alpha-2-Ländercode -> deutscher Name (Auszug der
/// gängigsten Software-Herkunftsländer; unbekannte Codes bleiben der Code selbst).</summary>
public static class Countries
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "USA",              ["DE"] = "Deutschland",     ["GB"] = "Großbritannien",
        ["UK"] = "Großbritannien",  ["IE"] = "Irland",          ["FR"] = "Frankreich",
        ["NL"] = "Niederlande",     ["CH"] = "Schweiz",         ["AT"] = "Österreich",
        ["BE"] = "Belgien",         ["IT"] = "Italien",         ["ES"] = "Spanien",
        ["PT"] = "Portugal",        ["SE"] = "Schweden",        ["NO"] = "Norwegen",
        ["FI"] = "Finnland",        ["DK"] = "Dänemark",        ["PL"] = "Polen",
        ["CZ"] = "Tschechien",      ["SK"] = "Slowakei",        ["HU"] = "Ungarn",
        ["RO"] = "Rumänien",        ["BG"] = "Bulgarien",       ["GR"] = "Griechenland",
        ["EE"] = "Estland",         ["LV"] = "Lettland",        ["LT"] = "Litauen",
        ["UA"] = "Ukraine",         ["RU"] = "Russland",        ["BY"] = "Belarus",
        ["TR"] = "Türkei",          ["IL"] = "Israel",          ["AE"] = "V.A. Emirate",
        ["IN"] = "Indien",          ["CN"] = "China",           ["HK"] = "Hongkong",
        ["TW"] = "Taiwan",          ["JP"] = "Japan",           ["KR"] = "Südkorea",
        ["SG"] = "Singapur",        ["MY"] = "Malaysia",        ["ID"] = "Indonesien",
        ["TH"] = "Thailand",        ["VN"] = "Vietnam",         ["PH"] = "Philippinen",
        ["AU"] = "Australien",      ["NZ"] = "Neuseeland",      ["CA"] = "Kanada",
        ["MX"] = "Mexiko",          ["BR"] = "Brasilien",       ["AR"] = "Argentinien",
        ["CL"] = "Chile",           ["ZA"] = "Südafrika",       ["EG"] = "Ägypten",
        ["LU"] = "Luxemburg",       ["IS"] = "Island",          ["HR"] = "Kroatien",
        ["SI"] = "Slowenien",       ["RS"] = "Serbien",         ["CY"] = "Zypern",
        ["MT"] = "Malta",           ["LI"] = "Liechtenstein",   ["MC"] = "Monaco",
    };

    public static string Name(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        return Names.TryGetValue(code, out var n) ? n : code.ToUpperInvariant();
    }
}
