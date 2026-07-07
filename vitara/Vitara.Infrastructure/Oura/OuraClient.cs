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
    private const string BASE = "https://api.ouraring.com/v2/usercollection";

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

    // ── Auth ──

    public async Task<string> ExchangeCodeAsync(string code)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = _redirectUri, ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
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
            ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId, ["client_secret"] = _clientSecret,
        };
        var resp = await _http.PostAsync("https://api.ouraring.com/oauth/token", new FormUrlEncodedContent(form));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<OuraTokenResponse>(_json)
            ?? throw new InvalidOperationException("Empty refresh response");
        return JsonSerializer.Serialize(body, _json);
    }

    // ── Personal Info ──

    public async Task<UserProfile> GetPersonalInfoAsync(string accessToken)
    {
        var doc = await GetJsonAsync($"{BASE}/personal_info", accessToken);
        return new UserProfile
        {
            Id = "default",
            Age = doc.RootElement.TryGetNullable<int>("age"),
            Weight = doc.RootElement.TryGetNullable<double>("weight"),
            Height = doc.RootElement.TryGetNullable<double>("height"),
            BiologicalSex = doc.RootElement.TryGetProperty("biological_sex", out var sex) ? sex.GetString() : null,
            Email = doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    // ── Sleep ──

    public async Task<List<SleepSession>> GetSleepAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("sleep", accessToken, from, to);
        var sessions = new List<SleepSession>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            sessions.Add(new SleepSession
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
            });
        }
        return sessions;
    }

    // ── Readiness ──

    public async Task<List<DailyReadiness>> GetReadinessAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("daily_readiness", accessToken, from, to);
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

    // ── Activity ──

    public async Task<List<DailyActivity>> GetActivityAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("daily_activity", accessToken, from, to);
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

    // ── Stress ──

    public async Task<List<DailyStress>> GetStressAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("daily_stress", accessToken, from, to);
        var list = new List<DailyStress>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new DailyStress
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Day = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                StressHighSeconds = item.TryGetNullable<int>("stress_high"),
                RecoveryHighSeconds = item.TryGetNullable<int>("recovery_high"),
                DaySummary = item.TryGetProperty("day_summary", out var ds) ? ds.GetString() : null,
            });
        }
        return list;
    }

    // ── Resilience ──

    public async Task<List<DailyResilience>> GetResilienceAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("daily_resilience", accessToken, from, to);
        var list = new List<DailyResilience>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var contributors = item.TryGetProperty("contributors", out var c) ? c : (JsonElement?)null;
            list.Add(new DailyResilience
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Day = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                Level = item.TryGetProperty("level", out var lvl) ? lvl.GetString() : null,
                SleepRecovery = contributors?.TryGetNullable<int>("sleep_recovery"),
                DaytimeRecovery = contributors?.TryGetNullable<int>("daytime_recovery"),
                Stress = contributors?.TryGetNullable<int>("stress"),
            });
        }
        return list;
    }

    // ── Cardiovascular Age ──

    public async Task<List<DailyCardiovascularAge>> GetCardiovascularAgeAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("daily_cardiovascular_age", accessToken, from, to);
        var list = new List<DailyCardiovascularAge>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new DailyCardiovascularAge
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Day = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                VascularAge = item.TryGetNullable<double>("vascular_age"),
            });
        }
        return list;
    }

    // ── SpO2 ──

    public async Task<List<DailySpo2>> GetSpo2Async(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("daily_spo2", accessToken, from, to);
        var list = new List<DailySpo2>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            double? avg = null;
            if (item.TryGetProperty("spo2_percentage", out var pct))
                avg = pct.TryGetNullable<double>("average");

            list.Add(new DailySpo2
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Day = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                Spo2Average = avg,
                BreathingDisturbanceIndex = item.TryGetNullable<double>("breathing_disturbance_index"),
            });
        }
        return list;
    }

    // ── Heart Rate ──

    public async Task<List<HeartRateSample>> GetHeartRateAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var url = $"{BASE}/heartrate?start_datetime={from:yyyy-MM-dd}T00:00:00&end_datetime={to:yyyy-MM-dd}T23:59:59";
        var doc = await GetJsonAsync(url, accessToken);
        var list = new List<HeartRateSample>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new HeartRateSample
            {
                Bpm = item.TryGet("bpm", out int bpm) ? bpm : 0,
                Timestamp = DateTime.Parse(item.GetProperty("timestamp").GetString() ?? ""),
                Source = item.TryGetProperty("source", out var src) ? src.GetString() : null,
            });
        }
        return list;
    }

    // ── VO2 Max ──

    public async Task<List<Vo2MaxRecord>> GetVo2MaxAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("vO2_max", accessToken, from, to);
        var list = new List<Vo2MaxRecord>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new Vo2MaxRecord
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Day = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                Vo2Max = item.TryGetNullable<double>("vo2_max"),
            });
        }
        return list;
    }

    // ── Workouts ──

    public async Task<List<Workout>> GetWorkoutsAsync(string accessToken, DateOnly from, DateOnly to)
    {
        var doc = await GetCollectionAsync("workout", accessToken, from, to);
        var list = new List<Workout>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            list.Add(new Workout
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Day = DateOnly.Parse(item.GetProperty("day").GetString() ?? ""),
                Activity = item.TryGetProperty("activity", out var act) ? act.GetString() ?? "" : "",
                StartTime = item.TryGetProperty("start_datetime", out var st) ? DateTime.Parse(st.GetString()!) : null,
                EndTime = item.TryGetProperty("end_datetime", out var et) ? DateTime.Parse(et.GetString()!) : null,
                Calories = item.TryGetNullable<int>("calories"),
                Distance = item.TryGetNullable<int>("distance"),
                Intensity = item.TryGetProperty("intensity", out var inten) ? inten.GetString() : null,
                Label = item.TryGetProperty("label", out var lbl) ? lbl.GetString() : null,
                Source = item.TryGetProperty("source", out var src) ? src.GetString() : null,
            });
        }
        return list;
    }

    // ── Helpers ──

    private async Task<JsonDocument> GetJsonAsync(string url, string accessToken)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    private Task<JsonDocument> GetCollectionAsync(string collection, string accessToken, DateOnly from, DateOnly to)
        => GetJsonAsync($"{BASE}/{collection}?start_date={from:yyyy-MM-dd}&end_date={to:yyyy-MM-dd}", accessToken);

    private record OuraTokenResponse(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]    int ExpiresIn,
        [property: JsonPropertyName("token_type")]    string TokenType
    );
}

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
