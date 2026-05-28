using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TorWall.Services;

/// Fetches the set of currently running Tor relay IPv4 addresses from the
/// official Onionoo service operated by The Tor Project.
public sealed class TorRelayFetcher
{
    private const string OnionooUrl =
        "https://onionoo.torproject.org/details?type=relay&running=true&fields=or_addresses";

    private readonly HttpClient _http;

    public TorRelayFetcher(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TorWall/1.0 (+https://github.com/)");
    }

    public async Task<IReadOnlyList<string>> FetchRelayIPv4Async(CancellationToken ct = default)
    {
        var doc = await _http.GetFromJsonAsync<OnionooResponse>(OnionooUrl, ct).ConfigureAwait(false)
                  ?? throw new InvalidOperationException("Empty response from onionoo.");

        var ips = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relay in doc.Relays)
        {
            if (relay.OrAddresses is null) continue;
            foreach (var raw in relay.OrAddresses)
            {
                if (TryParseIPv4(raw, out var ip)) ips.Add(ip);
            }
        }
        return ips.ToList();
    }

    /// or_addresses entries look like "1.2.3.4:9001" or "[2001:db8::1]:9001".
    /// We keep only the IPv4 form because Windows Firewall handles v6 in a
    /// separate ruleset and the v4 set is enough to reach the Tor network.
    private static bool TryParseIPv4(string entry, out string ipv4)
    {
        ipv4 = string.Empty;
        if (string.IsNullOrEmpty(entry) || entry[0] == '[') return false;
        var colon = entry.LastIndexOf(':');
        var host = colon > 0 ? entry[..colon] : entry;
        if (!IPAddress.TryParse(host, out var addr)) return false;
        if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        ipv4 = addr.ToString();
        return true;
    }

    private sealed class OnionooResponse
    {
        [JsonPropertyName("relays")] public List<Relay> Relays { get; set; } = new();
    }

    private sealed class Relay
    {
        [JsonPropertyName("or_addresses")] public List<string>? OrAddresses { get; set; }
    }
}
