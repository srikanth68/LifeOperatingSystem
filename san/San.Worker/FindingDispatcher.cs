using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.Worker;

// Shared by SystemAuditWorker and EmailTriageWorker: parse a model reply into keyed
// findings, drop the ones still inside their cooldown, and send what's left as ONE
// message. Both go through here on a single key namespace, so the same bill spotted
// in an email and in the Vault snapshot notifies once, not twice.
public static class FindingDispatcher
{
    public static async Task<int> DispatchAsync(
        string reply,
        string source,
        ISanRepository repo,
        ITelegramNotifier telegram,
        IModuleContextService moduleContext,
        ILogger logger,
        CancellationToken ct)
    {
        var findings = FindingParser.Parse(reply);
        if (findings.Count == 0)
        {
            logger.LogInformation("{Source}: nothing to report.", source);
            return 0;
        }

        var now = DateTime.UtcNow;
        var ledger = await repo.GetLedgerAsync();
        var due = new List<(AgentFinding Finding, string Key)>();
        var usedThisRun = new List<(string Key, IReadOnlySet<string> Tokens)>();

        foreach (var f in findings)
        {
            // Resolve to an EXISTING topic before trusting the model's key, so a
            // reworded repeat lands on the row that's already counting.
            var (key, entry) = ResolveKey(f, ledger, usedThisRun, logger, source);

            if (entry is not null &&
                !NotifyPolicy.ShouldNotify(f.Severity, entry.NotifyCount, entry.LastNotifiedAt, f.DueOn, now))
            {
                var wait = NotifyPolicy.Cooldown(f.Severity, entry.NotifyCount, f.DueOn, now) - (now - entry.LastNotifiedAt);
                logger.LogInformation("{Source}: suppressing '{Key}' ({Severity}, told {Count}x) — {Hours:F1}h of cooldown left.",
                    source, key, f.Severity, entry.NotifyCount, wait.TotalHours);
                continue;
            }

            usedThisRun.Add((key, TopicSignature.Tokens(f.Message)));
            due.Add((f, key));
        }

        if (due.Count == 0)
        {
            logger.LogInformation("{Source}: {Total} finding(s), all within cooldown — staying quiet.", source, findings.Count);
            return 0;
        }

        var body = string.Join("\n", due.Select(x => $"{Icon(x.Finding.Severity)} {x.Finding.Message}"));
        var header = source == "audit" ? "🔎 System audit" : "📬 Email triage";
        if (telegram.IsConfigured)
            await telegram.SendAsync($"{header}:\n{body}", ct);

        foreach (var (f, key) in due)
            await repo.RecordNotificationAsync(new NotificationLedgerEntry
            {
                Key = key,
                Severity = f.Severity,
                LastMessage = f.Message,
                Source = source,
                NotifyCount = 1,          // ignored on update — the repo increments
                FirstSeenAt = now,
                LastNotifiedAt = now,
                DueOn = f.DueOn,
            });

        await moduleContext.SaveKnowledgeAsync($"san-{source}", source, body, ct);
        logger.LogInformation("{Source}: sent {Sent} of {Total} finding(s).", source, due.Count, findings.Count);
        return due.Count;
    }

    // Exact key match first (cheap, and correct when the model does behave), then
    // content similarity against everything the ledger already knows, then anything
    // already emitted in this same run. Only if none of those hit does the model's
    // key get used as-is.
    private static (string Key, NotificationLedgerEntry? Entry) ResolveKey(
        AgentFinding f,
        List<NotificationLedgerEntry> ledger,
        List<(string Key, IReadOnlySet<string> Tokens)> usedThisRun,
        ILogger logger,
        string source)
    {
        var exact = ledger.FirstOrDefault(e => e.Key == f.Key);
        if (exact is not null) return (f.Key, exact);

        var tokens = TopicSignature.Tokens(f.Message);
        if (tokens.Count < TopicSignature.MinTokens) return (f.Key, null);

        NotificationLedgerEntry? best = null;
        var bestScore = 0.0;
        foreach (var e in ledger)
        {
            var score = TopicSignature.Similarity(tokens, TopicSignature.Tokens(e.LastMessage));
            if (score > bestScore) { bestScore = score; best = e; }
        }

        if (best is not null && bestScore >= TopicSignature.SameTopicThreshold)
        {
            logger.LogInformation("{Source}: '{New}' matches existing topic '{Key}' ({Score:P0}) — reusing it.",
                source, f.Key, best.Key, bestScore);
            return (best.Key, best);
        }

        // Two rewordings of the same thing arriving in ONE reply would otherwise both
        // send, since neither is in the ledger yet.
        foreach (var (key, used) in usedThisRun)
            if (TopicSignature.Similarity(tokens, used) >= TopicSignature.SameTopicThreshold)
            {
                logger.LogInformation("{Source}: '{New}' duplicates '{Key}' within this run — dropping.", source, f.Key, key);
                return (key, new NotificationLedgerEntry
                {
                    Key = key, NotifyCount = 1, LastNotifiedAt = DateTime.UtcNow, Severity = f.Severity,
                });
            }

        return (f.Key, null);
    }

    private static string Icon(string severity) => severity switch
    {
        "critical" => "🔴",
        "high" => "🟠",
        "low" => "⚪",
        _ => "🟡",
    };
}
