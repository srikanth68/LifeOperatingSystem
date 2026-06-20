using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.Infrastructure.Oura;

public class OuraClient : IOuraClient
{
    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OuraClient(IHttpClientFactory factory, IConfiguration cfg)
    {
        _http = factory.CreateClient("Oura");
        _clientId     = cfg["Oura:ClientId"]     ?? throw new InvalidOperationException("Oura:ClientId missing");
        _clientSecret = cfg["Oura:ClientSecret"] ?? throw new InvalidOperationException("Oura:ClientSecret missing");
        _redirectUri  = cfg["Oura:RedirectUri"]  ?? "http://localhost:5100/api/oura/callback";
    }

    public async Task<string> ExchangeCodeAsync(string code)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = _redirectUri,
            ["client_id"]     = _clientId,
            ["client_secret"] = _clientSecret,
        };
        var resp = await _http.PostAsync("https://api.ouraring.com/oauth/token", new FormUrlEncodedContent(form));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<OuraTokenResponse>(_json)
            ?? throw new InvalidOperationException("Empty token response");
        return JsonSerializer.Serialize(body, _json);
    }

    public async Task<string> RefreshAccessTokenAsync(string refreshToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"]     = _clientId,
            ["client_secret"] = _clientSecret,
        };
        var resp = await _http.PostAsync("https://api.ouraring.com/oauth/token", new FormUrlEncodedContent(form));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<OuraTokenResponse>(_json)
            ?? throw new InvalidOperationException("Empty refresh response");
        return JsonSerializer.Serialize(body, _json);
    }

    public async Task<List<SleepSession>> GetSleepAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.ouraring.com/v2/usercollection/sleep?start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var sessions = new List<SleepSession>();

        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var s = new SleepSession
            {
                Id   = item.GetProperty("id").GetString() ?? "",
                Day  = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                BedtimeStart = DateTime.Parse(item.GetProperty("bedtime_start").GetString() ?? ""),
                BedtimeEnd   = DateTime.Parse(item.GetProperty("bedtime_end").GetString() ?? ""),
                TotalSleepMinutes = item.TryGet("total_sleep_duration", out int tsd) ? tsd / 60 : 0,
                RemMinutes   = item.TryGet("rem_sleep_duration",  out int rem) ? rem  / 60 : 0,
                DeepMinutes  = item.TryGet("deep_sleep_duration", out int deep) ? deep / 60 : 0,
                LightMinutes = item.TryGet("light_sleep_duration", out int light) ? light / 60 : 0,
                AwakeMinutes = item.TryGet("awake_time", out int awake) ? awake / 60 : 0,
                Score        = item.TryGetNullable<int>("score"),
                AvgHrv       = item.TryGetNullable<double>("average_hrv"),
                LowestHr     = item.TryGetNullable<double>("lowest_heart_rate"),
                AvgBreathingRate = item.TryGetNullable<double>("average_breath"),
                AvgSpo2      = item.TryGetNullable<double>("average_spo2"),
                SkinTempDeviation = item.TryGetNullable<double>("skin_temp_deviation"),
            };
            sessions.Add(s);
        }
        return sessions;
    }

    public async Task<List<DailyReadiness>> GetReadinessAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.ouraring.com/v2/usercollection/daily_readiness?start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var list = new List<DailyReadiness>();

        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var contributors = item.TryGetProperty("contributors", out var c) ? c : (JsonElement?)null;
            list.Add(new DailyReadiness
            {
                Id    = item.GetProperty("id").GetString() ?? "",
                Day   = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                Score = item.TryGetNullable<int>("score"),
                Level = item.TryGetNullable<int>("score") switch { >= 85 => "optimal", >= 70 => "good", _ => "pay_attention" },
                HrvBalance           = contributors?.TryGetNullable<int>("hrv_balance"),
                RecoveryIndex        = contributors?.TryGetNullable<int>("recovery_index"),
                RestingHeartRate     = contributors?.TryGetNullable<int>("resting_heart_rate"),
                ActivityBalance      = contributors?.TryGetNullable<int>("activity_balance"),
                SleepBalance         = contributors?.TryGetNullable<int>("sleep_balance"),
                TemperatureDeviation = contributors?.TryGetNullable<int>("temperature_deviation"),
            });
        }
        return list;
    }

    public async Task<List<DailyActivity>> GetActivityAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.ouraring.com/v2/usercollection/daily_activity?start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var list = new List<DailyActivity>();

        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new DailyActivity
            {
                Id             = item.GetProperty("id").GetString() ?? "",
                Day            = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                Score          = item.TryGetNullable<int>("score"),
                Steps          = item.TryGet("steps", out int steps) ? steps : 0,
                ActiveCalories = item.TryGet("active_calories", out int ac) ? ac : 0,
                TotalCalories  = item.TryGet("total_calories", out int tc) ? tc : 0,
                EquivalentWalkingDistance = item.TryGet("equivalent_walking_distance", out int ewd) ? ewd : 0,
                HighActivityMinutes   = item.TryGet("high_activity_time", out int hat) ? hat / 60 : 0,
                MediumActivityMinutes = item.TryGet("medium_activity_time", out int mat) ? mat / 60 : 0,
                LowActivityMinutes    = item.TryGet("low_activity_time", out int lat) ? lat / 60 : 0,
                SedentaryMinutes      = item.TryGet("sedentary_time", out int sed) ? sed / 60 : 0,
                RestMinutes           = item.TryGet("rest_time", out int rest) ? rest / 60 : 0,
            });
        }
        return list;
    }

    private record OuraTokenResponse(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]    int ExpiresIn,
        [property: JsonPropertyName("token_type")]    string TokenType
    );
}

// JSON helper extensions
internal static class JsonElementExtensions
{
    public static bool TryGet<T>(this JsonElement el, string prop, out T value) where T : struct
    {
        if (el.TryGetProperty(prop, out var p) && p.ValueKind != JsonValueKind.Null)
        {
            try { value = (T)Convert.ChangeType(p.GetRawText().Trim('"'), typeof(T)); return true; }
            catch { }
        }
        value = default; return false;
    }

    public static T? TryGetNullable<T>(this JsonElement el, string prop) where T : struct
        => el.TryGet<T>(prop, out var v) ? v : null;

    public static T? TryGetNullable<T>(this JsonElement? el, string prop) where T : struct
        => el.HasValue ? el.Value.TryGetNullable<T>(prop) : null;
}
