using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    private const string SystemPrompt =
        "You are San, the personal life-assistant module inside Maaya OS, triaging the user's email. " +
        "You will be given a batch of new emails (sender, subject, snippet, received time). For each one " +
        "that is genuinely actionable or important — a bill, a deadline, something from a real person " +
        "needing a reply, a property-related issue, a scheduled event — decide if it warrants creating a " +
        "reminder, alert, calendar event, or (for property-related items) a property task, and call the " +
        "appropriate tool. Ignore newsletters, marketing, and routine notifications entirely — do not " +
        "create anything for them and do not mention them. " +
        "After handling actionable emails, reply with ONLY a short plain-text summary (a few lines max) " +
        "of what's worth the user's attention right now, suitable to send verbatim as a Telegram message. " +
        "If nothing in the batch is worth mentioning, reply with exactly: NOTHING_IMPORTANT";

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
            var systemPrompt = SystemPrompt + "\n\n" + timeContext;

            var reply = await chat.CompleteWithToolsAsync(systemPrompt, [userTurn], tools, executor);
            logger.LogInformation("Email triage reply ({Length} chars): {Preview}",
                reply.Length, reply.Length > 500 ? reply[..500] + "…" : reply);

            if (telegram.IsConfigured && !reply.Contains("NOTHING_IMPORTANT"))
                await telegram.SendAsync($"📬 Email triage:\n{reply.Trim()}", ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email triage run failed");
        }
    }
}
