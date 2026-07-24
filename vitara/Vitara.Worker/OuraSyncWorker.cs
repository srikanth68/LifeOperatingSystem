using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitara.Application.Interfaces;

namespace Vitara.Worker;

public class OuraSyncWorker(IServiceProvider services, ILogger<OuraSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Vitara sync worker started. Interval: {h}h", SyncInterval.TotalHours);
        await SyncAsync(ct);

        using var timer = new PeriodicTimer(SyncInterval);
        while (await timer.WaitForNextTickAsync(ct))
            await SyncAsync(ct);
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting Oura sync at {time}", DateTimeOffset.UtcNow);
        try
        {
            using var scope = services.CreateScope();
            var repo   = scope.ServiceProvider.GetRequiredService<IVitaraRepository>();
            var client = scope.ServiceProvider.GetRequiredService<IOuraClient>();

            var token = await repo.GetTokenAsync();
            if (token is null) { logger.LogWarning("No Oura token — skipping sync"); return; }

            if (token.ExpiresAt - DateTime.UtcNow < TimeSpan.FromHours(1))
            {
                logger.LogInformation("Token expiring soon, refreshing...");
                var refreshed = await client.RefreshAccessTokenAsync(token.RefreshToken);
                var payload   = JsonSerializer.Deserialize<TokenPayload>(refreshed, _json)!;
                token.AccessToken  = payload.AccessToken;
                token.RefreshToken = payload.RefreshToken;
                token.ExpiresAt    = DateTime.UtcNow.AddSeconds(payload.ExpiresIn);
                await repo.SaveTokenAsync(token);
            }

            var to   = DateOnly.FromDateTime(DateTime.UtcNow);
            var lastDay = await repo.GetLatestDayAsync();
            var from = lastDay?.AddDays(-1) ?? to.AddDays(-30);

            // Profile (always fetch — lightweight, no date range)
            await SafeSync("profile", async () =>
            {
                var profile = await client.GetPersonalInfoAsync(token.AccessToken);
                await repo.SaveProfileAsync(profile);
                logger.LogInformation("Profile synced: age={a}", profile.Age);
            });

            // Core data
            var sleep     = await SafeSync("sleep",      () => client.GetSleepAsync(token.AccessToken, from, to));
            var readiness = await SafeSync("readiness",  () => client.GetReadinessAsync(token.AccessToken, from, to));
            var activity  = await SafeSync("activity",   () => client.GetActivityAsync(token.AccessToken, from, to));

            // Extended data
            var stress     = await SafeSync("stress",     () => client.GetStressAsync(token.AccessToken, from, to));
            var resilience = await SafeSync("resilience", () => client.GetResilienceAsync(token.AccessToken, from, to));
            var cvAge      = await SafeSync("cv-age",     () => client.GetCardiovascularAgeAsync(token.AccessToken, from, to));
            var spo2       = await SafeSync("spo2",       () => client.GetSpo2Async(token.AccessToken, from, to));
            var vo2        = await SafeSync("vo2max",     () => client.GetVo2MaxAsync(token.AccessToken, from, to));
            var workouts   = await SafeSync("workouts",   () => client.GetWorkoutsAsync(token.AccessToken, from, to));

            // Heart rate — only last 2 days (high volume)
            var hrFrom = to.AddDays(-2);
            var heartRate = await SafeSync("heartrate", () => client.GetHeartRateAsync(token.AccessToken, hrFrom, to));

            // Persist all
            if (sleep?.Count > 0)      await repo.UpsertSleepAsync(sleep);
            if (readiness?.Count > 0)  await repo.UpsertReadinessAsync(readiness);
            if (activity?.Count > 0)   await repo.UpsertActivityAsync(activity);
            if (stress?.Count > 0)     await repo.UpsertStressAsync(stress);
            if (resilience?.Count > 0) await repo.UpsertResilienceAsync(resilience);
            if (cvAge?.Count > 0)      await repo.UpsertCardiovascularAgeAsync(cvAge);
            if (spo2?.Count > 0)       await repo.UpsertSpo2Async(spo2);
            if (vo2?.Count > 0)        await repo.UpsertVo2MaxAsync(vo2);
            if (workouts?.Count > 0)   await repo.UpsertWorkoutsAsync(workouts);
            if (heartRate?.Count > 0)  await repo.UpsertHeartRateAsync(heartRate);

            token.LastSyncedAt = DateTime.UtcNow;
            await repo.SaveTokenAsync(token);

            logger.LogInformation("Sync complete — sleep:{s} readiness:{r} activity:{a} stress:{st} resilience:{re} cvAge:{cv} spo2:{sp} vo2:{v2} workouts:{w} hr:{hr}",
                sleep?.Count ?? 0, readiness?.Count ?? 0, activity?.Count ?? 0,
                stress?.Count ?? 0, resilience?.Count ?? 0, cvAge?.Count ?? 0,
                spo2?.Count ?? 0, vo2?.Count ?? 0, workouts?.Count ?? 0, heartRate?.Count ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync failed");
        }
    }

    private async Task<List<T>?> SafeSync<T>(string name, Func<Task<List<T>>> fetch)
    {
        try { return await fetch(); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to sync {name} — endpoint may not be available for this account", name); return null; }
    }

    private async Task SafeSync(string name, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to sync {name}", name); }
    }

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private record TokenPayload(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]    int ExpiresIn
    );
}
