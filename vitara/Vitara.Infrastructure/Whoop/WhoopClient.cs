using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Vitara.Application.Interfaces;

namespace Vitara.Infrastructure.Whoop;

// WHOOP developer API v2 (https://developer.whoop.com/api). OAuth 2 authorization code with
// the `offline` scope for a refresh token; access tokens last about an hour, so every sync
// refreshes first. Collections page 25 records at a time with a next_token cursor.
public class WhoopClient : IWhoopClient
{
    private const string Api = "https://api.prod.whoop.com/developer/v2";
    private const string AuthUrl = "https://api.prod.whoop.com/oauth/oauth2/auth";
    private const string TokenUrl = "https://api.prod.whoop.com/oauth/oauth2/token";

    // Read-only, and only what Vitara uses. `offline` is what returns a refresh token.
    public const string Scopes = "offline read:recovery read:sleep read:workout read:cycles";

    private readonly HttpClient _http;
    private readonly string _clientId, _clientSecret, _redirectUri;

    public WhoopClient(IHttpClientFactory factory, IConfiguration cfg)
    {
        _http = factory.CreateClient("Whoop");
        _clientId = cfg["Whoop:ClientId"] ?? "";
        _clientSecret = cfg["Whoop:ClientSecret"] ?? "";
        _redirectUri = cfg["Whoop:RedirectUri"] ?? "http://localhost:5100/api/whoop/callback";
    }

    public string AuthorizeUrl(string state) =>
        $"{AuthUrl}?response_type=code&client_id={Uri.EscapeDataString(_clientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(_redirectUri)}&scope={Uri.EscapeDataString(Scopes)}" +
        $"&state={Uri.EscapeDataString(state)}";

    public Task<WhoopTokens> ExchangeCodeAsync(string code) => TokenAsync(new()
    {
        ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = _redirectUri,
        ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
    });

    public Task<WhoopTokens> RefreshAsync(string refreshToken) => TokenAsync(new()
    {
        ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["scope"] = "offline",
        ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
    });

    private async Task<WhoopTokens> TokenAsync(Dictionary<string, string> form)
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            throw new InvalidOperationException("WHOOP_CLIENT_ID and WHOOP_CLIENT_SECRET are not set on the server.");

        var resp = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"WHOOP token request failed: HTTP {(int)resp.StatusCode} — {body}");

        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        return new WhoopTokens(
            r.GetProperty("access_token").GetString()!,
            r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
            r.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600);
    }

    public async Task<WhoopBundle> FetchAsync(string accessToken, DateTimeOffset from, DateTimeOffset to)
    {
        var sleeps = await CollectAsync(accessToken, "/activity/sleep", from, to);
        var recoveries = await CollectAsync(accessToken, "/recovery", from, to);
        var workouts = await CollectAsync(accessToken, "/activity/workout", from, to);
        return WhoopMapper.Map(sleeps, recoveries, workouts);
    }

    // Every page of one collection. Capped, so a cursor that never ends cannot loop forever.
    private async Task<List<JsonElement>> CollectAsync(string token, string path, DateTimeOffset from, DateTimeOffset to)
    {
        var all = new List<JsonElement>();
        string? next = null;
        for (var page = 0; page < 40; page++)
        {
            var url = $"{Api}{path}?limit=25&start={Uri.EscapeDataString(from.UtcDateTime.ToString("o"))}" +
                      $"&end={Uri.EscapeDataString(to.UtcDateTime.ToString("o"))}" +
                      (next is null ? "" : $"&nextToken={Uri.EscapeDataString(next)}");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"WHOOP {path} failed: HTTP {(int)resp.StatusCode} — {body}");

            using var doc = JsonDocument.Parse(body);
            foreach (var rec in doc.RootElement.GetProperty("records").EnumerateArray()) all.Add(rec.Clone());
            next = doc.RootElement.TryGetProperty("next_token", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString() : null;
            if (string.IsNullOrEmpty(next)) break;
        }
        return all;
    }
}
