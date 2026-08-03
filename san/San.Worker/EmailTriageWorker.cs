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
    private const string NothingImportant = EmailTriageDefaults.NothingImportant;

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
            if (accounts.Count == 0) return;

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

                    logger.LogInformation("{Count} new message(s) in {Email}", messages.Count, account.EmailAddress);
                    foreach (var m in messages)
                        batch.Add($"[{account.EmailAddress}] From: {m.From} | Subject: {m.Subject} | Received: {m.ReceivedAtUtc:u}\n{m.Snippet}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to fetch mail for {Email} — leaving its cursor untouched, will retry next run", account.EmailAddress);
                }
            }

            if (batch.Count == 0) return;

            var timeContext = await moduleContext.BuildTimeContextAsync(null, ct);
            var userTurn = new ChatTurn("user", "New emails since last check:\n\n" + string.Join("\n\n", batch));
            var (tools, executor) = await toolRouter.ResolveAsync(ct);
            var basePrompt = await repo.GetSettingAsync(EmailTriageDefaults.PromptKey) ?? EmailTriageDefaults.Prompt;
            var systemPrompt = basePrompt + "\n\n" + timeContext;

            // enableThinking: triage has to actually read each email and judge it —
            // important vs noise, and whether it warrants a reminder/alert/event/task.
            // This runs on a 15-minute timer with nobody waiting, so the extra
            // deliberation tokens cost nothing that matters here.
            var reply = await chat.CompleteWithToolsAsync(
                systemPrompt, [userTurn], tools, executor, maxSteps: 10, enableThinking: true, ct: ct);
            logger.LogInformation("Email triage reply ({Length} chars): {Preview}",
                reply.Length, reply.Length > 500 ? reply[..500] + "…" : reply);

            // The sentinel must be the ENTIRE reply, not merely present in it. A
            // substring check let "NOTHING_IMPORTANT except a bill due Friday" suppress
            // the whole message — silently dropping the one thing worth sending. Erring
            // toward a stray notification beats erring toward a missed bill.
            var trimmed = reply.Trim();
            if (trimmed.Equals(NothingImportant, StringComparison.OrdinalIgnoreCase) || trimmed.Length == 0)
            {
                logger.LogInformation("Email triage: nothing worth flagging in {Count} message(s).", batch.Count);
                return;
            }

            if (telegram.IsConfigured)
                await telegram.SendAsync($"📬 Email triage:\n{trimmed}", ct);

            // Record it in the brain too, so San's own time context surfaces what came
            // in on the next turn rather than the summary living only in Telegram.
            await moduleContext.SaveKnowledgeAsync("san-email", "email", trimmed, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email triage run failed");
        }
    }
}
