using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DGKNInspector.Services;

/// <summary>
/// Ermittelt Herkunftsland und Provider (ISP) zu öffentlichen IP-Adressen.
///
/// Hinweis zum Datenschutz: Öffentliche Remote-IPs werden an den kostenlosen
/// Dienst ip-api.com gesendet, um Land/Provider zu bestimmen. Private und
/// lokale Adressen verlassen den Rechner NICHT — sie werden hier direkt
/// beschriftet. Die Länderabfrage lässt sich in der App abschalten.
/// </summary>
public sealed class GeoIpService
{
    public sealed record GeoInfo(string Country, string CountryCode, string Isp);

    private static readonly GeoInfo Local   = new("Lokal", "",   "");
    private static readonly GeoInfo Private = new("Privates Netz", "", "");
    private static readonly GeoInfo Unknown = new("—", "", "");

    private readonly Dictionary<string, GeoInfo> _cache = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private sealed class ApiRow
    {
        [JsonPropertyName("status")]      public string? Status { get; set; }
        [JsonPropertyName("country")]     public string? Country { get; set; }
        [JsonPropertyName("countryCode")] public string? CountryCode { get; set; }
        [JsonPropertyName("isp")]         public string? Isp { get; set; }
        [JsonPropertyName("org")]         public string? Org { get; set; }
        [JsonPropertyName("query")]       public string? Query { get; set; }
    }

    public GeoInfo? FromCache(string ip) => _cache.TryGetValue(ip, out var g) ? g : null;

    public static bool IsPrivateOrLocal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;

        byte[] b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            if (b[0] == 10) return true;                          // 10.0.0.0/8
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true;          // 192.168.0.0/16
            if (b[0] == 169 && b[1] == 254) return true;          // link-local
            if (b[0] == 127) return true;
            if (b[0] == 0) return true;
        }
        else if (b.Length == 16)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            if (b[0] == 0xfc || b[0] == 0xfd) return true;        // unique local fc00::/7
            // IPv4-mapped ::ffff:x.x.x.x
            bool mapped = true;
            for (int i = 0; i < 10; i++) if (b[i] != 0) { mapped = false; break; }
            if (mapped && b[10] == 0xff && b[11] == 0xff)
                return IsPrivateOrLocal(new IPAddress(new[] { b[12], b[13], b[14], b[15] }));
        }
        return false;
    }

    /// <summary>Fragt fehlende IPs in Batches ab und füllt den Cache.
    /// Bereits bekannte oder private IPs werden übersprungen.</summary>
    public async Task ResolveManyAsync(IEnumerable<IPAddress> ips, CancellationToken ct = default)
    {
        var todo = new List<string>();
        foreach (var ip in ips)
        {
            if (IsPrivateOrLocal(ip))
            {
                string key = ip.ToString();
                if (!_cache.ContainsKey(key))
                    _cache[key] = IPAddress.IsLoopback(ip) ? Local : Private;
                continue;
            }
            string s = ip.ToString();
            if (!_cache.ContainsKey(s) && !todo.Contains(s))
                todo.Add(s);
        }

        // ip-api Batch: max. 100 pro Anfrage, ~15 Anfragen/Minute frei.
        foreach (var chunk in Chunk(todo, 100))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var payload = chunk.ConvertAll(q => new { query = q, fields = "status,country,countryCode,isp,org,query" });
                using var resp = await _http.PostAsJsonAsync("http://ip-api.com/batch", payload, ct);
                if (!resp.IsSuccessStatusCode) continue;

                var rows = await resp.Content.ReadFromJsonAsync<List<ApiRow>>(ct);
                if (rows == null) continue;

                foreach (var r in rows)
                {
                    if (string.IsNullOrEmpty(r.Query)) continue;
                    if (r.Status == "success")
                    {
                        string isp = !string.IsNullOrWhiteSpace(r.Isp) ? r.Isp!
                                   : (r.Org ?? "");
                        _cache[r.Query] = new GeoInfo(r.Country ?? "—", r.CountryCode ?? "", isp);
                    }
                    else
                    {
                        _cache[r.Query] = Unknown;
                    }
                }
            }
            catch
            {
                // Netzwerkfehler ignorieren — Land bleibt dann einfach "—".
            }
        }
    }

    private static IEnumerable<List<string>> Chunk(List<string> src, int n)
    {
        for (int i = 0; i < src.Count; i += n)
            yield return src.GetRange(i, Math.Min(n, src.Count - i));
    }
}
