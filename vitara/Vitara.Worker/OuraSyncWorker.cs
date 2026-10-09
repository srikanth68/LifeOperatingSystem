using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Profiles;

namespace Vitara.Worker;

public class OuraSyncWorker(IServiceProvider services, ILogger<OuraSyncWorker> logger) : BackgroundService
{
    // Same daily-at-configurable-time pattern as Vault.Worker/Jobs/ScheduledSyncWorker.cs.
    private readonly TimeSpan _syncTime = ParseSyncTime(Environment.GetEnvironmentVariable("SYNC_TIME") ?? "10:00");

    // A failed sync used to wait a full day for its next attempt. On a box reached over
    // a mesh VPN that drops and re-establishes on its own, that turns a few minutes of
    // network trouble into a missing day of health data.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(2);

    // Heart rate is the only table Oura fills continuously and the only one worth
    // bounding. Ninety days is well past any window the dashboard or San asks for.
    private static readonly int HeartRateRetentionDays =
        int.TryParse(Environment.GetEnvironmentVariable("HEARTRATE_RETENTION_DAYS"), out var d) && d > 0 ? d : 90;

    // How far back to reach for a collection that has never returned anything.
    private const int ColdStartDays = 30;

    private static TimeSpan ParseSyncTime(string s)
    {
        var parts = s.Split(':');
        return new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), 0);
    }

    private DateTime GetNextRunTime(DateTime now)
    {
        var nextRun = now.Date.Add(_syncTime);
        if (nextRun <= now) nextRun = nextRun.AddDays(1);
        return nextRun;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Vitara sync worker started. Sync time set to {t:hh\\:mm}", _syncTime);

        while (!ct.IsCancellationRequested)
        {
            var now = LocalTime.Now;
            var nextRunTime = GetNextRunTime(now);
            logger.LogInformation("Next Oura sync scheduled for {next:yyyy-MM-dd HH:mm:ss} (in {h:F1}h)",
                nextRunTime, (nextRunTime - now).TotalHours);

            try { await Task.Delay(nextRunTime - now, ct); }
            catch (OperationCanceledException) { break; }

            var ok = await SyncAsync(ct);

            // Come back soon after a failure instead of tomorrow. The next scheduled run
            // still happens; this only adds attempts in between.
            while (!ok && !ct.IsCancellationRequested && LocalTime.Now < GetNextRunTime(LocalTime.Now).AddDays(-1).AddHours(23))
            {
                logger.LogWarning("Sync did not succeed — retrying in {h}h.", RetryAfterFailure.TotalHours);
                try { await Task.Delay(RetryAfterFailure, ct); }
                catch (OperationCanceledException) { return; }
                ok = await SyncAsync(ct);
            }
        }
    }

    // Returns true when at least one collection came back with data.
    // Everyone with a linked ring, each against their own database.
    //
    // Succeeds if ANY profile wrote data, which keeps the retry behaviour exactly as it was
    // for one person: the worker comes back soon after a failure, and stays quiet after a
    // success. A profile with no ring linked is not a failure and says nothing.
    private async Task<bool> SyncAsync(CancellationToken ct)
    {
        var outcomes = new List<bool?>();

        await ProfileScopes.ForEachAsync(services,
            async (scope, id) => outcomes.Add(await SyncProfileAsync(scope, id, ct)), logger);

        return outcomes.Any(o => o == true);
    }

    // null means "no ring linked for this person", which is not a failure.
    private async Task<bool?> SyncProfileAsync(IServiceScope scope, string profileId, CancellationToken ct)
    {
        var repo = scope.ServiceProvider.GetRequiredService<IVitaraRepository>();
        var client = scope.ServiceProvider.GetRequiredService<IOuraClient>();

        var token = await repo.GetTokenAsync();
        if (token is null)
        {
            logger.LogDebug("Profile {Profile} has no Oura ring linked — skipping.", profileId);
            return null;
        }

        logger.LogInformation("Starting Oura sync for profile {Profile} at {time}", profileId, DateTimeOffset.UtcNow);

        // Recorded before anything can go wrong, so an attempt that dies halfway still
        // leaves a trace. An attempt far newer than the last success is what tells the
        // difference between "sync is broken" and "sync is not running".
        token.LastSyncAttemptAt = DateTime.UtcNow;

        var failures = new List<string>();

        try
        {
            if (token.ExpiresAt - DateTime.UtcNow < TimeSpan.FromHours(1))
            {
                logger.LogInformation("Token expiring soon, refreshing...");
                var refreshed = await client.RefreshAccessTokenAsync(token.RefreshToken);
                var payload = JsonSerializer.Deserialize<TokenPayload>(refreshed, _json)!;
                token.AccessToken = payload.AccessToken;
                token.RefreshToken = payload.RefreshToken;
                token.ExpiresAt = DateTime.UtcNow.AddSeconds(payload.ExpiresIn);
                await repo.SaveTokenAsync(token);
            }
        }
        catch (Exception ex)
        {
            // Oura rotates refresh tokens. A refresh that fails can leave the link
            // permanently dead, and there is no notifier here to say so -- recording it
            // on the token is the only way anyone finds out before noticing the data
            // stopped.
            logger.LogError(ex, "Oura token refresh failed — the link may need re-authorising.");
            token.LastSyncError = $"Token refresh failed: {ex.Message}";
            await repo.SaveTokenAsync(token);
            return false;
        }

        var to = LocalTime.Today;

        // One window per collection, from its OWN newest day. A single shared watermark
        // taken across sleep/readiness/activity stranded the other six: once the core
        // three moved on, days a failing collection had missed were never requested
        // again.
        var watermarks = await repo.GetLatestDaysAsync();
        DateOnly From(string collection) =>
            watermarks.TryGetValue(collection, out var day) ? day.AddDays(-1) : to.AddDays(-ColdStartDays);

        await SafeSync("profile", async () =>
        {
            var incoming = await client.GetPersonalInfoAsync(token.AccessToken);

            // MERGED, not overwritten. This used to save Oura's answer over the whole row, so
            // a height entered by hand was erased the first night Oura had none to offer. The
            // sync now only fills what the person has not set themselves, and never writes an
            // empty value over a real one. See ProfileMerge.
            await repo.SaveProfileAsync(ProfileMerge.FromSync(await repo.GetProfileAsync(), incoming));
            logger.LogInformation("Profile synced: age={a}", incoming.Age);
        }, failures);

        // Oura's label for each sleep period (long_sleep, late_nap ...) was not stored before, so
        // recent nights have none. While any does, sleep is fetched from the cold-start window
        // again and the upsert writes the labels in; once they are all labelled this is the
        // usual window. Nights older than that keep no label and fall back to the longest.
        var sleepFrom = SleepNights.NeedsLabels(await repo.GetSleepAsync(to.AddDays(-ColdStartDays), to))
            ? to.AddDays(-ColdStartDays)
            : From("sleep");
        var sleep = await SafeSync("sleep", () => client.GetSleepAsync(token.AccessToken, sleepFrom, to), failures);
        var readiness = await SafeSync("readiness", () => client.GetReadinessAsync(token.AccessToken, From("readiness"), to), failures);
        var activity = await SafeSync("activity", () => client.GetActivityAsync(token.AccessToken, From("activity"), to), failures);
        var stress = await SafeSync("stress", () => client.GetStressAsync(token.AccessToken, From("stress"), to), failures);
        var resilience = await SafeSync("resilience", () => client.GetResilienceAsync(token.AccessToken, From("resilience"), to), failures);
        var cvAge = await SafeSync("cv-age", () => client.GetCardiovascularAgeAsync(token.AccessToken, From("cv-age"), to), failures);
        var spo2 = await SafeSync("spo2", () => client.GetSpo2Async(token.AccessToken, From("spo2"), to), failures);
        var vo2 = await SafeSync("vo2max", () => client.GetVo2MaxAsync(token.AccessToken, From("vo2max"), to), failures);
        var workouts = await SafeSync("workouts", () => client.GetWorkoutsAsync(token.AccessToken, From("workouts"), to), failures);

        // Heart rate stays on a short window regardless of watermark — it is sampled
        // continuously, and backfilling a month of it would pull tens of thousands of
        // rows to fill gaps nothing reads.
        var heartRate = await SafeSync("heartrate",
            () => client.GetHeartRateAsync(token.AccessToken, to.AddDays(-2), to), failures);

        if (sleep?.Count > 0) await repo.UpsertSleepAsync(sleep);
        if (readiness?.Count > 0) await repo.UpsertReadinessAsync(readiness);
        if (activity?.Count > 0) await repo.UpsertActivityAsync(activity);
        if (stress?.Count > 0) await repo.UpsertStressAsync(stress);
        if (resilience?.Count > 0) await repo.UpsertResilienceAsync(resilience);
        if (cvAge?.Count > 0) await repo.UpsertCardiovascularAgeAsync(cvAge);
        if (spo2?.Count > 0) await repo.UpsertSpo2Async(spo2);
        if (vo2?.Count > 0) await repo.UpsertVo2MaxAsync(vo2);
        if (workouts?.Count > 0) await repo.UpsertWorkoutsAsync(workouts);
        if (heartRate?.Count > 0) await repo.UpsertHeartRateAsync(heartRate);

        var wrote = new[] { sleep?.Count, readiness?.Count, activity?.Count, stress?.Count, resilience?.Count,
                            cvAge?.Count, spo2?.Count, vo2?.Count, workouts?.Count, heartRate?.Count }
            .Any(c => c > 0);

        try
        {
            var pruned = await repo.PruneHeartRateAsync(DateTime.UtcNow.AddDays(-HeartRateRetentionDays));
            if (pruned > 0) logger.LogInformation("Pruned {n} heart-rate samples older than {d} days.", pruned, HeartRateRetentionDays);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Heart-rate prune failed."); }

        // Only moved when something was actually written. Stamping it on every attempt
        // is what let the UI report "synced today" while the ring data was a week old.
        if (wrote) token.LastSyncedAt = DateTime.UtcNow;
        token.LastSyncError = failures.Count == 0 ? null : string.Join("; ", failures);
        await repo.SaveTokenAsync(token);

        logger.LogInformation("Sync complete — sleep:{s} readiness:{r} activity:{a} stress:{st} resilience:{re} cvAge:{cv} spo2:{sp} vo2:{v2} workouts:{w} hr:{hr}{fail}",
            sleep?.Count ?? 0, readiness?.Count ?? 0, activity?.Count ?? 0,
            stress?.Count ?? 0, resilience?.Count ?? 0, cvAge?.Count ?? 0,
            spo2?.Count ?? 0, vo2?.Count ?? 0, workouts?.Count ?? 0, heartRate?.Count ?? 0,
            failures.Count == 0 ? "" : $" — FAILED: {string.Join(", ", failures)}");

        return wrote;
    }

    private async Task<List<T>?> SafeSync<T>(string name, Func<Task<List<T>>> fetch, List<string> failures)
    {
        try { return await fetch(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync {name} — endpoint may not be available for this account", name);
            failures.Add(name);
            return null;
        }
    }

    private async Task SafeSync(string name, Func<Task> action, List<string> failures)
    {
        try { await action(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync {name}", name);
            failures.Add(name);
        }
    }

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private record TokenPayload(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]    int ExpiresIn
    );
}
