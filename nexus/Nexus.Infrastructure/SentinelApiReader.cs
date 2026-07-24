using System.Net;
using System.Text.Json;
using Nexus.Application.Dtos;
using Nexus.Application.Interfaces;

namespace Nexus.Infrastructure;

// HTTP reader for a *remote* Sentinel that exposes its own JSON API (e.g. the
// Python engine on another machine, reached here over the NordVPN tunnel at
// http://127.0.0.1:8787). This is the network-topology alternative to
// SentinelDbReader (which reads a co-located sentinel.db file directly).
//
// The remote JSON is expected to match API_CONTRACT.md's camelCase shapes, which
// already line up 1:1 with the DTOs — so responses deserialize straight through.
//
// Config (env):
//   SENTINEL_API_URL    = http://127.0.0.1:8787   (required to select this reader)
//   SENTINEL_API_PREFIX = ""                       (path prefix before /board etc.;
//                                                    set to "/api/nexus/sentinel" or
//                                                    "/api" if the remote nests them)
public class SentinelApiReader : ISentinelReader
{
    private readonly HttpClient _http;
    private readonly string _prefix;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SentinelApiReader(HttpClient http, string baseUrl, string prefix)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _prefix = "/" + prefix.Trim('/'); // "" -> "/", "api" -> "/api"
        if (_prefix == "/") _prefix = "";
    }

    // Availability is determined per-call (a live HTTP probe would block); the
    // controller only reacts to SentinelUnavailableException, so this stays true.
    public bool DatabaseAvailable => true;

    private string Path(string rel) => $"{_prefix}/{rel.TrimStart('/')}";

    private async Task<HttpResponseMessage> GetAsync(string rel)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.GetAsync(Path(rel));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SentinelUnavailableException(
                $"Sentinel engine unreachable at {_http.BaseAddress} — is it running and is the VPN tunnel up? ({ex.Message})");
        }

        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
            throw new SentinelUnavailableException("Sentinel engine reports it hasn't initialized yet (503).");

        return resp;
    }

    private async Task<T> GetJsonAsync<T>(string rel) where T : new()
    {
        using var resp = await GetAsync(rel);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, _json) ?? new T();
    }

    public Task<List<BoardRow>> GetBoardAsync() => GetJsonAsync<List<BoardRow>>("board");

    public async Task<string?> GetTickerDetailJsonAsync(string symbol)
    {
        using var resp = await GetAsync($"tickers/{Uri.EscapeDataString(symbol)}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        // Pass the frozen detail_json through verbatim, per API_CONTRACT.md §3.
        return await resp.Content.ReadAsStringAsync();
    }

    public Task<List<HistoryPoint>> GetHistoryAsync(string symbol, int limit) =>
        GetJsonAsync<List<HistoryPoint>>($"tickers/{Uri.EscapeDataString(symbol)}/history?limit={limit}");

    public Task<List<AlertItem>> GetAlertsAsync(DateTime? since, int limit)
    {
        var q = $"alerts?limit={limit}";
        if (since is { } s) q += $"&since={Uri.EscapeDataString(s.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}";
        return GetJsonAsync<List<AlertItem>>(q);
    }

    public Task<List<PositionRow>> GetPositionsAsync() => GetJsonAsync<List<PositionRow>>("positions");

    public Task<List<WatchItem>> GetWatchlistAsync() => GetJsonAsync<List<WatchItem>>("watchlist");

    public async Task<StatusDto> GetStatusAsync()
    {
        using var resp = await GetAsync("status");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<StatusDto>(body, _json)
            ?? throw new SentinelUnavailableException("Sentinel /status returned an empty body.");
    }
}
