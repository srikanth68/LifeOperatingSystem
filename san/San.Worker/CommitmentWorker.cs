using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// Chases the things the user said they would do.
//
// NorthStar's ActionItem has always had DueDate, Priority and Status — everything
// needed to notice a commitment going stale — and nothing ever read it except on
// request. So San could record "you said you'd call the insurance company" and then
// never mention it again. Recording an obligation and never raising it is arguably
// worse than not recording it, because it creates the impression something is being
// watched.
//
// Runs on a slow timer. Commitments do not change every fifteen minutes, and the
// point of this worker is to be the thing that quietly remembers — not another
// source of noise. All the cadence control lives in the shared ledger: keyed
// suppression, widening cooldowns the longer something is ignored, deadline-aware
// backoff, and quiet hours. This worker only decides what is stale.
public class CommitmentWorker(IServiceProvider services, ILogger<CommitmentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(
        double.TryParse(Environment.GetEnvironmentVariable("COMMITMENT_INTERVAL_HOURS"), out var h) && h > 0 ? h : 4);

    // Offset from every other worker's startup so a restart doesn't fire five
    // different timers into the model and the network at once.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Commitment worker started. Interval: {h}h (first run in {m}m)",
            Interval.TotalHours, StartupDelay.TotalMinutes);

        try { await Task.Delay(StartupDelay, ct); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunAsync(ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        string? error = null;
        try
        {
            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISanRepository>();
            var telegram = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
            var moduleContext = scope.ServiceProvider.GetRequiredService<IModuleContextService>();

            var commitments = await moduleContext.GetOpenCommitmentsAsync(ct);
            if (commitments.Count == 0)
            {
                logger.LogInformation("Commitments: nothing open.");
                return;
            }

            // "Today" in the user's timezone, not the container's — whether something
            // is overdue changes at local midnight.
            var tz = await moduleContext.ResolveTimeZoneAsync(ct);
            var nowUtc = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz));

            var findings = Commitments.EvaluateAll(commitments, today, nowUtc).ToList();
            logger.LogInformation("Commitments: {Open} open, {Stale} worth raising.",
                commitments.Count, findings.Count);

            if (findings.Count == 0) return;

            await FindingDispatcher.DispatchFindingsAsync(
                findings, "commitments", repo, telegram, moduleContext, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Commitment run failed");
            error = ex.Message;
        }
        finally
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IHealthTracker>()
                    .RecordAsync(HealthComponents.WorkerCommitments, error is null, error, ct);
            }
            catch { /* bookkeeping must never be why a run counts as failed */ }
        }
    }
}
