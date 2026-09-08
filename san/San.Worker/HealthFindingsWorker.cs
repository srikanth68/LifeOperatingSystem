using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.DTOs;
using San.Application.Interfaces;

namespace San.Worker;

// Carries Vitara's health findings to the user, through the same ledger as everything
// else San says.
//
// The last link in a chain that was built from the bottom and stopped one step short.
// Oura syncs, the projector flattens, the calculator baselines, the detectors conclude
// -- and until this existed, nothing read the conclusions. The statistics were correct
// and the system was silent.
//
// The adapter is small on purpose. FindingDispatcher already does the hard part:
// keyed deduplication, cooldowns that lengthen when something is ignored, and the
// separation between "message the user" and "tell NorthStar". Health findings arrive
// with a key derived in code precisely so they can use it. Building a second
// notification path here would mean a second cooldown to tune and a second place for
// the same finding to arrive twice.
public class HealthFindingsWorker(IServiceProvider services, ILogger<HealthFindingsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(
        double.TryParse(Environment.GetEnvironmentVariable("HEALTH_FINDINGS_CHECK_HOURS"), out var h) && h > 0 ? h : 3);

    // Behind Vitara's own derived-metrics pass, which runs six minutes after start and
    // then hourly. Reading before it has computed anything just means an empty list and
    // a wasted call.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(9);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Health findings worker started. Checking every {h}h.", Interval.TotalHours);

        try { await Task.Delay(StartupDelay, ct); } catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Health findings pass failed."); }

            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var moduleContext = sp.GetRequiredService<IModuleContextService>();
        var findings = await moduleContext.GetHealthFindingsAsync(ct);

        if (findings.Count == 0)
        {
            logger.LogDebug("Health findings: nothing active.");
            return;
        }

        await FindingDispatcher.DispatchFindingsAsync(
            findings.Select(HealthFindingMapping.ToAgentFinding).ToList(),
            "health-intel",
            sp.GetRequiredService<ISanRepository>(),
            sp.GetRequiredService<ITelegramNotifier>(),
            moduleContext,
            logger,
            ct);
    }
}
