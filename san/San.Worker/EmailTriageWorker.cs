using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;
using San.Domain.Entities;
using San.Infrastructure.Agent;

namespace San.Worker;

// Polls every connected mailbox (San.API's EmailController owns the OAuth connect flow;
// this only reads what's already connected) and reads each new email properly:
//
//   1. Bulk mail is dropped by rule (EmailFilter) before any model sees it.
//   2. Every other email is read IN FULL, one at a time, beside what NorthStar already
//      knows that relates to it -- earlier emails from the same sender or thread, the
//      entities it names, matching memories (see EmailBrain).
//   3. The model acts with the same tools chat uses (reminders, events, property
//      tasks) and says what the email was, what it teaches, and what it corrects.
//   4. The worker writes that to NorthStar: a dated event with the email as its key,
//      linked to the entities it concerns; proposed memories sourced to the email; and
//      proposed corrections of memories it contradicts. The email itself is not stored.
//
// Findings from the whole run go out as ONE Telegram message: a quiet inbox stays
// quiet, and ten emails do not become ten pings.
public class EmailTriageWorker(IServiceProvider services, ILogger<EmailTriageWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    // Reading in full is a model call per email. A backlog (a first connect, a week
    // away) is worked through over several runs rather than in one that never ends:
    // the mailbox cursor holds at the first email left unread.
    private const int MaxReadsPerRun = 12;

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
            var telegram = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
            var moduleContext = scope.ServiceProvider.GetRequiredService<IModuleContextService>();
            var health = scope.ServiceProvider.GetRequiredService<IHealthTracker>();

            var accounts = (await repo.GetEmailAccountsAsync()).Where(a => a.Active).ToList();
            if (accounts.Count == 0)
            {
                // Say so, and record the run as healthy. Returning in silence made "no
                // mailbox has ever been connected" look exactly like "the timer is dead".
                logger.LogInformation(
                    "Email triage: no active mailbox connected, nothing to do. "
                    + "Connect one from Settings (GET /api/email/accounts lists what is linked).");
                await health.RecordAsync(HealthComponents.WorkerEmailTriage, true, ct: ct);
                return;
            }

            // Read once per run, not per message. Editable in Settings so a sender that
            // keeps being filtered wrongly can be fixed without a rebuild.
            var keepSenders = await repo.GetSettingAsync(EmailFilter.KeepSendersKey);
            var dropSenders = await repo.GetSettingAsync(EmailFilter.DropSendersKey);

            var run = new RunState(scope.ServiceProvider, repo, moduleContext);
            var runStartedAt = DateTime.UtcNow;

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
                    await ReadMailboxAsync(account, provider, run, keepSenders, dropSenders, runStartedAt, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to read mail for {Email} — its cursor holds, will retry next run", account.EmailAddress);
                }
            }

            // Before the empty exit: reading every mailbox and finding nothing new is a
            // completed run, and must look different from a timer that has died.
            await health.RecordAsync(HealthComponents.WorkerEmailTriage, true, ct: ct);

            // Closing runs BEFORE raising, deliberately. If one email confirms a payment
            // and another chases it, the reminder should end the run completed rather
            // than recreated -- DuplicateGuard only stops a second copy of something
            // still open, so the order is what makes the two agree.
            if (run.Settlements.Count > 0)
                await SettlementCloser.ApplyAsync(run.Settlements, repo, telegram, moduleContext, logger, ct);

            // Keyed findings through the shared ledger -- same key namespace as the
            // system audit, so a bill both of them notice is reported once.
            if (run.Findings.Count > 0)
                await FindingDispatcher.DispatchFindingsAsync(run.Findings, "email", repo, telegram, moduleContext, logger, ct);
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

    private async Task ReadMailboxAsync(
        EmailAccount account, IEmailProviderClient provider, RunState run,
        string? keepSenders, string? dropSenders, DateTime runStartedAt, CancellationToken ct)
    {
        var since = account.LastCheckedAt ?? account.CreatedAt;
        var (updatedTokenJson, messages) = await provider.FetchNewMessagesAsync(account, since);

        // The token now, before anything else can fail: refresh tokens rotate, and
        // losing the new one locks the mailbox out until it is reconnected.
        await run.Repo.UpdateEmailAccountAsync(account.Id, a => a.TokenJson = updatedTokenJson);

        var keys = messages.Select(m => KeyOf(account.Provider, m)).ToList();
        var done = await run.Repo.GetProcessedEmailKeysAsync(keys);

        DateTime? holdAt = null;
        int read = 0, filtered = 0, deferred = 0;

        try
        {
            foreach (var m in messages.OrderBy(m => m.ReceivedAtUtc))
            {
                var key = KeyOf(account.Provider, m);
                if (done.Contains(key)) continue;

                // Bulk mail is removed here rather than described to the model and
                // hoped about. Every drop is logged with its reason: a filter that
                // silently ate a real bill would be worse than the spam it prevents.
                var verdict = EmailFilter.Classify(m, keepSenders, dropSenders);
                if (!verdict.Keep)
                {
                    logger.LogInformation("Filtered out \"{Subject}\" from {From} — {Reason}.",
                        Trim(m.Subject), Trim(m.From), verdict.Reason);
                    await run.Repo.MarkEmailProcessedAsync(key, account.EmailAddress, "filtered");
                    filtered++;
                    continue;
                }

                if (run.Reads >= MaxReadsPerRun)
                {
                    holdAt ??= m.ReceivedAtUtc.AddSeconds(-1);
                    deferred++;
                    continue;
                }

                // Held here before the read: if it throws, the next run starts at this
                // email rather than past it.
                holdAt ??= m.ReceivedAtUtc.AddSeconds(-1);
                var outcome = await ReadOneAsync(account, m, run, ct);
                await run.Repo.MarkEmailProcessedAsync(key, account.EmailAddress, outcome);
                run.Reads++;
                read++;
                if (deferred == 0) holdAt = null;
            }
        }
        finally
        {
            // Past everything read; held at the first email still to read, if any.
            var cursor = holdAt ?? runStartedAt;
            await run.Repo.UpdateEmailAccountAsync(account.Id, a => a.LastCheckedAt = cursor);
        }

        if (messages.Count > 0)
            logger.LogInformation(
                "{Email}: {Count} new — {Read} read, {Filtered} filtered as bulk, {Deferred} left for the next run.",
                account.EmailAddress, messages.Count, read, filtered, deferred);
    }

    private async Task<string> ReadOneAsync(EmailAccount account, EmailMessage m, RunState run, CancellationToken ct)
    {
        var (senderName, senderAddress) = EmailSender.Parse(m.From);
        var hasBody = !string.IsNullOrWhiteSpace(m.Body);
        var body = EmailText.Clean(EmailText.ToPlainText(hasBody ? m.Body : m.Snippet, hasBody && m.BodyIsHtml));

        var related = await run.Context.GetRelatedAsync(
            $"{m.Subject}\n{body}",
            senderAddress is null ? [] : [senderAddress],
            m.ThreadId, ct);

        var turn = EmailBrainPrompt.UserTurn(
            account.EmailAddress, m.From, m.Subject, m.ReceivedAtUtc, body, EmailBrainPrompt.Related(related));

        var (system, tools, executor) = await run.PromptAndToolsAsync(ct);
        var reply = await run.Chat.CompleteWithToolsAsync(
            system, [new ChatTurn("user", turn)], tools, executor,
            // Thinking on: this has to actually read and judge an email, and nobody is
            // waiting on a 15-minute timer.
            maxSteps: 8, enableThinking: true, ct: ct);

        run.Findings.AddRange(FindingParser.Parse(reply));
        run.Settlements.AddRange(Settlements.Parse(reply));

        var notes = EmailNotes.Parse(reply);
        logger.LogInformation("Read \"{Subject}\" from {From}: {Importance}{Summary}",
            Trim(m.Subject), Trim(m.From), notes.Importance, notes.Summary is null ? "" : $" — {notes.Summary}");
        if (!notes.Keep) return notes.Importance;

        await RememberAsync(account, m, senderName, senderAddress, notes, related, run.Context, ct);
        return notes.Importance;
    }

    // What the email teaches, written to NorthStar with the email as its source. All of
    // it best-effort: a NorthStar outage costs the brain an email, never the reminders
    // the model already made.
    private async Task RememberAsync(
        EmailAccount account, EmailMessage m, string? senderName, string? senderAddress,
        EmailNotes notes, System.Text.Json.JsonElement? related, IModuleContextService context, CancellationToken ct)
    {
        var messageId = m.Id ?? $"{senderAddress}:{m.ReceivedAtUtc:O}";
        var sourceRef = EmailBrainPrompt.SourceRef(account.Provider, messageId);
        var evidence = $"Email \"{m.Subject}\" from {senderName ?? senderAddress ?? m.From}, {m.ReceivedAtUtc:yyyy-MM-dd}";

        var title = notes.Summary ?? $"Email from {senderName ?? senderAddress}: {m.Subject}";
        var eventId = await context.RecordEventAsync(
            "email", "email", Cap(title, 300),
            EmailBrainPrompt.Detail(senderAddress, m.ThreadId, m.Subject),
            m.ReceivedAtUtc, EmailBrainPrompt.EventKey(account.Provider, messageId), ct);

        // What it is about: what the model named, what NorthStar recognised in the
        // text, and -- when the email mattered enough to act on or remember -- the
        // sender, so the next email from them is read beside this one.
        var about = notes.Remember.SelectMany(r => r.About)
            .Concat(EmailBrainPrompt.RelatedEntityNames(related))
            .Concat(senderName is not null && (notes.Importance == EmailNotes.Act || notes.Remember.Count > 0)
                ? [senderName] : [])
            .ToList();
        if (eventId is { } id && about.Count > 0)
            await context.LinkToEntitiesAsync("event", id, about, "san", sourceRef, ct);

        foreach (var r in notes.Remember)
        {
            if (!MemoryWorthKeeping.Keep(r.Text, out var why))
            {
                logger.LogInformation("Email memory rejected ({Why}): {Text}", why, r.Text);
                continue;
            }
            await context.SaveMemoryAsync(r.Text, r.Kind, 3,
                new MemoryOrigin(MemoryOrigin.Proposed, sourceRef, evidence, r.About), ct);
        }

        var shown = EmailBrainPrompt.RelatedMemoryKinds(related);
        foreach (var c in notes.Corrects)
        {
            if (!shown.TryGetValue(c.Memory, out var kind))
            {
                logger.LogInformation("Correction ignored: memory {Id} was not among those shown for this email.", c.Memory);
                continue;
            }
            await context.SaveMemoryAsync(c.Text, kind, 3,
                new MemoryOrigin(MemoryOrigin.Proposed, sourceRef, evidence, Supersedes: c.Memory), ct);
        }
    }

    private static string KeyOf(string provider, EmailMessage m) =>
        $"{provider}:{m.Id ?? $"{m.From}|{m.Subject}|{m.ReceivedAtUtc:O}"}";

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];

    // Log lines only — a 200-character marketing subject should not make the filter's
    // decisions unreadable in the logs.
    private static string Trim(string? s) =>
        string.IsNullOrEmpty(s) ? "(none)" : s.Length > 70 ? s[..70] + "…" : s;

    // Everything one run shares across mailboxes: the model, the tools (resolved once,
    // and only if something is read), and the findings and settlements gathered for
    // the single notification at the end.
    private sealed class RunState(IServiceProvider sp, ISanRepository repo, IModuleContextService context)
    {
        public ISanRepository Repo { get; } = repo;
        public IModuleContextService Context { get; } = context;
        public IChatProvider Chat { get; } = sp.GetRequiredService<IChatProvider>();
        public List<AgentFinding> Findings { get; } = [];
        public List<Settlement> Settlements { get; } = [];
        public int Reads { get; set; }

        private (string, List<ToolDefinition>, Func<ToolCall, CancellationToken, Task<string>>)? resolved;

        public async Task<(string System, List<ToolDefinition> Tools, Func<ToolCall, CancellationToken, Task<string>> Executor)>
            PromptAndToolsAsync(CancellationToken ct)
        {
            if (resolved is { } r) return r;

            var (allTools, executor) = await sp.GetRequiredService<AgentToolRouter>().ResolveAsync(ct);
            // A tool the model can see is a tool it eventually reaches for: the
            // withheld ones are taken off the table, not just discouraged.
            var tools = allTools.Where(t => !EmailTriageDefaults.WithheldTools.Contains(t.Name)).ToList();

            var basePrompt = await Repo.GetSettingAsync(EmailTriageDefaults.PromptKey) ?? EmailTriageDefaults.Prompt;
            var timeContext = await Context.BuildTimeContextAsync(null, ct);
            var system = basePrompt + "\n\n" + timeContext + "\n\n" + SanOutputConventions.Text
                         + "\n\n" + EmailTriageDefaults.OutputContract;

            resolved = (system, tools, executor);
            return resolved.Value;
        }
    }
}
