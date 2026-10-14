using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitara.Application.Interfaces;
using Vitara.Infrastructure.Profiles;
using Vitara.Infrastructure.Whoop;

namespace Vitara.Worker;

// WHOOP for everyone who linked one (ADR 0029). Every four hours rather than once a day:
// WHOOP scores a night some time after waking, and an access token lasts only an hour, so
// a frequent light sync is both fresher and kinder to a refresh token that rotates.
public class WhoopSyncWorker(IServiceProvider services, ILogger<WhoopSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(4);
    private const int Days = 7;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Out of step with the Oura worker's start, so the two never share a minute.
        try { await Task.Delay(TimeSpan.FromMinutes(3), ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            await ProfileScopes.ForEachAsync(services, async (scope, id) =>
            {
                var repo = scope.ServiceProvider.GetRequiredService<IVitaraRepository>();
                var token = await repo.GetWhoopTokenAsync();
                if (token is null) return;            // no strap linked for this person: nothing to say

                var r = await WhoopSync.RunAsync(repo, scope.ServiceProvider.GetRequiredService<IWhoopClient>(), token, Days);
                if (r.Ok) logger.LogInformation("WHOOP sync for {Profile}: {Sleep} sleeps, {Ready} recoveries, {Workouts} workouts.",
                                                id, r.Sleep, r.Readiness, r.Workouts);
                else logger.LogWarning("WHOOP sync for {Profile} failed: {Error}", id, r.Error);
            }, logger);

            try { await Task.Delay(Every, ct); } catch (OperationCanceledException) { return; }
        }
    }
}
