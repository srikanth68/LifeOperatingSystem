using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;
using San.Infrastructure.Agent;

namespace San.Worker;

// Polls every connected mailbox (San.API's EmailController owns the OAuth connect flow;
// this only reads what's already connected), hands new messages to Gemma with the same
// tool catalog chat uses, and lets it decide what's worth a reminder/alert/calendar
// event/property task versus just a mention in the Telegram summary. One combined
// Telegram push per run, not one per email — a quiet inbox should stay quiet.
public class EmailTriageWorker(IServiceProvider services, ILogger<EmailTriageWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    // Prompt + sentinel live in San.Application (EmailTriageDefaults) because San.API
    // exposes the editor for them — keeping one copy stops the two from drifting.
    // The sentinel is interpreted by FindingParser, not here.

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("Email triage worker started. Interval: {m}m", CheckInterval.TotalMinutes);

        using var timer = new PeriodicTimer(CheckInterval);
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
            var providers = scope.ServiceProvider.GetServices<IEmailProviderClient>().ToList();
            var chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();
            var toolRouter = scope.ServiceProvider.GetRequiredService<AgentToolRouter>();
            var telegram = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
            var moduleContext = scope.ServiceProvider.GetRequiredService<IModuleContextService>();

            var accounts = (await repo.GetEmailAccountsAsync()).Where(a => a.Active).ToList();
            if (accounts.Count == 0)
            {
                // Say so, and record the run as healthy.
                //
                // This used to return in silence, which made "no mailbox has ever been
                // connected" look exactly like "the timer is dead" from the outside --
                // no Telegram, no log line, no health entry. The same reasoning already
                // moved the health record above the empty-batch exit further down; this
                // branch was simply missed, and it is the one that fires when the OAuth
                // connect was never completed.
                logger.LogInformation(
                    "Email triage: no active mailbox connected, nothing to do. "
                    + "Connect one from Settings (GET /api/email/accounts lists what is linked).");
                await scope.ServiceProvider.GetRequiredService<IHealthTracker>()
                    .RecordAsync(HealthComponents.WorkerEmailTriage, true, ct: ct);
                return;
            }

            // Read once per run, not per message. Editable in Settings so a sender that
            // keeps being filtered wrongly can be fixed without a rebuild.
            var keepSenders = await repo.GetSettingAsync(EmailFilter.KeepSendersKey);
            var dropSenders = await repo.GetSettingAsync(EmailFilter.DropSendersKey);

            var runStartedAt = DateTime.UtcNow;
            var batch = new List<string>();

            foreach (var account in accounts)
            {
                var provider = providers.FirstOrDefault(p => p.Provider == account.Provider);
                if (provider is null)
                {
                    logger.LogWarning("No client registered for provider {Provider} (account {Email})", account.Provider, account.EmailAddress);
                    continue;
                }

                try
                {
                    var since = account.LastCheckedAt ?? account.CreatedAt;
                    var (updatedTokenJson, messages) = await provider.FetchNewMessagesAsync(account, since);

                    await repo.UpdateEmailAccountAsync(account.Id, a =>
                    {
                        a.TokenJson = updatedTokenJson;
                        a.LastCheckedAt = runStartedAt;
                    });

                    if (messages.Count == 0) continue;

                    // Bulk mail is removed here rather than being described to the model
                    // and hoped about. Every drop is logged with its reason: a filter
                    // that silently ate a real bill would be worse than the spam it
                    // prevents, so it must always be possible to see what it took.
                    var kept = 0;
                    foreach (var m in messages)
                    {
                        var verdict = EmailFilter.Classify(m, keepSenders, dropSenders);
                        if (!verdict.Keep)
                        {
                            logger.LogInformation("Filtered out \"{Subject}\" from {From} — {Reason}.",
                                Trim(m.Subject), Trim(m.From), verdict.Reason);
                            continue;
                        }
                        kept++;
                        batch.Add($"[{account.EmailAddress}] From: {m.From} | Subject: {m.Subject} | Received: {m.ReceivedAtUtc:u}\n{m.Snippet}");
                    }

                    logger.LogInformation("{Kept} of {Count} new message(s) in {Email} reached triage.",
                        kept, messages.Count, account.EmailAddress);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to fetch mail for {Email} — leaving its cursor untouched, will retry next run", account.EmailAddress);
                }
            }

            // Before the empty-batch exit: fetching every mailbox and finding nothing new
            // is a completed run. Only recording at the bottom would make a healthy quiet
            // worker look identical to one whose timer had died.
            var health = scope.ServiceProvider.GetRequiredService<IHealthTracker>();
            await health.RecordAsync(HealthComponents.WorkerEmailTriage, true, ct: ct);

            if (batch.Count == 0) return;

            var timeContext = await moduleContext.BuildTimeContextAsync(null, ct);
            var userTurn = new ChatTurn("user", "New emails since last check:\n\n" + string.Join("\n\n", batch));
            var (tools, executor) = await toolRouter.ResolveAsync(ct);
            var basePrompt = await repo.GetSettingAsync(EmailTriageDefaults.PromptKey) ?? EmailTriageDefaults.Prompt;
            var systemPrompt = basePrompt + "\n\n" + timeContext + "\n\n" + SanOutputConventions.Text;

            // enableThinking: triage has to actually read each email and judge it —
            // important vs noise, and whether it warrants a reminder/alert/event/task.
            // This runs on a 15-minute timer with nobody waiting, so the extra
            // deliberation tokens cost nothing that matters here.
            var reply = await chat.CompleteWithToolsAsync(
                systemPrompt, [userTurn], tools, executor, maxSteps: 10, enableThinking: true, ct: ct);
            logger.LogInformation("Email triage reply ({Length} chars): {Preview}",
                reply.Length, reply.Length > 500 ? reply[..500] + "…" : reply);

            // Keyed findings through the shared ledger — same key namespace as the
            // system audit, so a bill both of them notice is reported once. Also
            // records to NorthStar so San's time context surfaces it in chat.
            await FindingDispatcher.DispatchAsync(reply, "email", repo, telegram, moduleContext, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email triage run failed");
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IHealthTracker>()
                    .RecordAsync(HealthComponents.WorkerEmailTriage, false, ex.Message, ct);
            }
            catch { /* the run already failed; bookkeeping must not mask it */ }
        }
    }

    // Log lines only — a 200-character marketing subject should not make the filter's
    // decisions unreadable in the logs.
    private static string Trim(string? s) =>
        string.IsNullOrEmpty(s) ? "(none)" : s.Length > 70 ? s[..70] + "…" : s;
}
