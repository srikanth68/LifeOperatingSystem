using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vitara.Application.Interfaces;

namespace Vitara.Infrastructure.Nutrition;

// Reads the food diary from the MyFitnessPal sidecar.
//
// The sidecar is a Python container holding the scraping client and its session cache;
// this only speaks HTTP to it over the compose network. Keeping the scraper in its own
// image means the thing most likely to break -- a site changing underneath an
// unofficial client -- cannot take a .NET build down with it.
//
// No authentication on this hop. The sidecar publishes no port and is reachable only
// from inside the compose network, which is also why it is the one service holding a
// real account password rather than a revocable token.
public class MfpNutritionClient(IHttpClientFactory httpFactory, ILogger<MfpNutritionClient> logger) : INutritionSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string? BaseUrl =>
        Environment.GetEnvironmentVariable("MFP_API_URL")?.TrimEnd('/') is { Length: > 0 } u ? u : null;

    // Blank config disables the feature rather than failing at it. A daily error about
    // a source nobody configured is noise, and this project has already learned what
    // noise does to a notification channel.
    public bool IsConfigured => BaseUrl is not null;

    public async Task<DiaryDay?> GetDiaryAsync(DateOnly day, CancellationToken ct = default)
    {
        if (BaseUrl is null) return null;

        try
        {
            var http = httpFactory.CreateClient("mfp");
            using var resp = await http.GetAsync($"{BaseUrl}/diary?date={day:yyyy-MM-dd}", ct);

            if (!resp.IsSuccessStatusCode)
            {
                // The sidecar answers 502 with the scraper's own error when the session
                // has died, which is the message worth surfacing -- "MFP login failed"
                // is actionable in a way that "HTTP 502" is not.
                var detail = await SafeReadErrorAsync(resp, ct);
                logger.LogWarning("MFP sidecar returned {Status} for {Day}: {Detail}",
                    (int)resp.StatusCode, day, detail);
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<DiaryDay>(Json, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reach the MFP sidecar for {Day}.", day);
            return null;
        }
    }

    private static async Task<string> SafeReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? body : body;
        }
        catch { return "(no detail)"; }
    }
}
