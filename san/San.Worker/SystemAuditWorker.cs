using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;
using San.Infrastructure.Agent;

namespace San.Worker;

// San watching the whole system on its own, rather than only answering when asked:
// every 15 minutes it reviews a live cross-module snapshot, digs in with the read
// tools where something looks off, and can create reminders/alerts/events/tasks
// directly. Findings go to Telegram and into NorthStar.
//
// Two things keep this from becoming noise:
//   - the previous run's findings are fed back in, so it won't re-report the same
//     thing every quarter hour
//   - a run that finds nothing new stays completely silent
//
// Deliberately offset from EmailTriageWorker's timer so the two don't hit the model
// at the same instant and stall an interactive chat between them.
public class SystemAuditWorker(IServiceProvider services, ILogger<SystemAuditWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(
        int.TryParse(Environment.GetEnvironmentVariable("AUDIT_INTERVAL_MINUTES"), out var m) && m > 0 ? m : 15);

    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(7);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("System audit worker started. Interval: {m}m (first run in {d}m)",
            Interval.TotalMinutes, StartupDelay.TotalMinutes);

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
        try
        {
            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISanRepository>();
            var chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();
            var toolRouter = scope.ServiceProvider.GetRequiredService<AgentToolRouter>();
            var telegram = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
            var moduleContext = scope.ServiceProvider.GetRequiredService<IModuleContextService>();

            var actions = scope.ServiceProvider.GetRequiredService<IChatActionService>();

            var timeContext = await moduleContext.BuildTimeContextAsync(null, ct);
            var context = await moduleContext.BuildChatContextAsync(ct);
            // San's own reminders/alerts/events — without these it re-flags things it
            // already scheduled, which was half the repetition.
            var ownContext = await actions.BuildOwnContextAsync();

            var basePrompt = await repo.GetSettingAsync(SystemAuditDefaults.PromptKey) ?? SystemAuditDefaults.Prompt;
            var systemPrompt = string.Join("\n\n", new[]
            {
                basePrompt, timeContext, context, ownContext, SanOutputConventions.Text,
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            var (tools, executor) = await toolRouter.ResolveAsync(ct);

            // Thinking on for the same reason email triage has it: this is judgment
            // over real data on a background timer, with nobody waiting on the reply.
            var reply = await chat.CompleteWithToolsAsync(
                systemPrompt,
                [new ChatTurn("user", "Run the audit now and report only what is new and worth my attention.")],
                tools, executor, maxSteps: 10, enableThinking: true, ct: ct);

            logger.LogInformation("System audit reply ({Length} chars): {Preview}",
                reply.Length, reply.Length > 500 ? reply[..500] + "…" : reply);

            // Suppression lives in the ledger now, not in the model's memory of what it
            // last said — see FindingDispatcher.
            await FindingDispatcher.DispatchAsync(reply, "audit", repo, telegram, moduleContext, logger, ct);

            // Marks the timer alive, not the findings useful. A run that reports nothing
            // is a healthy run; a worker whose timer died reports nothing too, and
            // without this the two are indistinguishable.
            var health = scope.ServiceProvider.GetRequiredService<IHealthTracker>();
            await health.RecordAsync(HealthComponents.WorkerAudit, true, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "System audit run failed");
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IHealthTracker>()
                    .RecordAsync(HealthComponents.WorkerAudit, false, ex.Message, ct);
            }
            catch { /* the run already failed; bookkeeping must not mask it */ }
        }
    }
}
