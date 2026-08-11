using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.Worker;

// Shared by SystemAuditWorker and EmailTriageWorker: parse a model reply into keyed
// findings, then answer TWO questions per finding, separately —
//
//   "does the user need a message about this right now?"  → NotifyPolicy (cooldown)
//   "does San's brain need to know about this?"           → KnowledgePolicy
//
// They used to be one question. NorthStar was written only when a Telegram message
// went out, so anything inside its cooldown was dropped whole: a finding that
// escalated or changed while quiet never reached the brain, and San would keep
// answering from the version it first saw. Now silence and ignorance are separate
// outcomes — a finding can be suppressed for the user and still update NorthStar.
//
// Both workers go through here on a single key namespace, so the same bill spotted
// in an email and in the Vault snapshot notifies once, not twice.
public static class FindingDispatcher
{
    public static Task<int> DispatchAsync(
        string reply,
        string source,
        ISanRepository repo,
        ITelegramNotifier telegram,
        IModuleContextService moduleContext,
        ILogger logger,
        CancellationToken ct) =>
        DispatchFindingsAsync(FindingParser.Parse(reply), source, repo, telegram, moduleContext, logger, ct);

    // Takes findings directly, for callers that DERIVE them rather than parse them out
    // of a model reply. San's own health checks come in this way: "San is running on
    // built-in tools" is exactly the report you cannot ask a possibly-broken San to
    // write for you, so it is computed deterministically and handed straight here —
    // where it still gets the same keyed suppression, cooldown, escalation and
    // NorthStar recording as everything else.
    public static async Task<int> DispatchFindingsAsync(
        IReadOnlyList<AgentFinding> findings,
        string source,
        ISanRepository repo,
        ITelegramNotifier telegram,
        IModuleContextService moduleContext,
        ILogger logger,
        CancellationToken ct)
    {
        if (findings.Count == 0)
        {
            logger.LogInformation("{Source}: nothing to report.", source);
            return 0;
        }

        var now = DateTime.UtcNow;
        var ledger = await repo.GetLedgerAsync();
        var usedThisRun = new List<(string Key, IReadOnlySet<string> Tokens)>();
        var sightings = new List<Sighting>();

        foreach (var f in findings)
        {
            // Resolve to an EXISTING topic before trusting the model's key, so a
            // reworded repeat lands on the row that's already counting.
            var (key, entry, duplicateInRun) = ResolveKey(f, ledger, usedThisRun, logger, source);
            if (duplicateInRun) continue; // same thing twice in one reply — not a second sighting

            var notify = entry is null ||
                         NotifyPolicy.ShouldNotify(f.Severity, entry.NotifyCount, entry.LastNotifiedAt, f.DueOn, now);

            if (!notify)
            {
                var wait = NotifyPolicy.Cooldown(f.Severity, entry!.NotifyCount, f.DueOn, now) - (now - entry.LastNotifiedAt);
                logger.LogInformation("{Source}: suppressing '{Key}' ({Severity}, told {Count}x) — {Hours:F1}h of cooldown left.",
                    source, key, f.Severity, entry.NotifyCount, wait.TotalHours);
            }

            // Asked regardless of the cooldown — that separation is the whole point.
            var record = KnowledgePolicy.ShouldRecord(f, entry, now, out var why);
            if (record && !notify)
                logger.LogInformation("{Source}: '{Key}' stays quiet but updates NorthStar — {Why}.", source, key, why);

            usedThisRun.Add((key, TopicSignature.Tokens(f.Message)));
            sightings.Add(new Sighting(f, key, notify, record));
        }

        var due = sightings.Where(s => s.Notify).ToList();

        if (due.Count > 0)
        {
            var body = string.Join("\n", due.Select(s => $"{Icon(s.Finding.Severity)} {s.Finding.Message}"));
            if (telegram.IsConfigured)
                await telegram.SendAsync($"{Header(source)}:\n{body}", ct);
        }

        // Per finding, not one blob of the batch: NorthStar keys knowledge by topic,
        // and a finding suppressed for the user has no batch to ride along with.
        foreach (var s in sightings.Where(s => s.Record))
            await moduleContext.SaveKnowledgeAsync($"san-{source}", s.Key, s.Finding.Message, ct);

        foreach (var s in sightings)
            await repo.RecordSightingAsync(new NotificationLedgerEntry
            {
                Key = s.Key,
                Severity = s.Finding.Severity,
                LastMessage = s.Finding.Message,
                Source = source,
                FirstSeenAt = now,      // ignored on update
                LastSeenAt = now,
                LastNotifiedAt = now,   // applied only when notified
                KnowledgeAt = now,      // applied only when recorded
                KnowledgeMessage = s.Finding.Message,
                DueOn = s.Finding.DueOn,
            }, s.Notify, s.Record);

        logger.LogInformation("{Source}: {Total} finding(s) — sent {Sent}, recorded {Recorded} to NorthStar.",
            source, sightings.Count, due.Count, sightings.Count(s => s.Record));
        return due.Count;
    }

    private readonly record struct Sighting(AgentFinding Finding, string Key, bool Notify, bool Record);

    // Exact key match first (cheap, and correct when the model does behave), then
    // content similarity against everything the ledger already knows, then anything
    // already emitted in this same run. Only if none of those hit does the model's
    // key get used as-is.
    private static (string Key, NotificationLedgerEntry? Entry, bool DuplicateInRun) ResolveKey(
        AgentFinding f,
        List<NotificationLedgerEntry> ledger,
        List<(string Key, IReadOnlySet<string> Tokens)> usedThisRun,
        ILogger logger,
        string source)
    {
        var exact = ledger.FirstOrDefault(e => e.Key == f.Key);
        if (exact is not null) return (f.Key, exact, false);

        var tokens = TopicSignature.Tokens(f.Message);
        if (tokens.Count < TopicSignature.MinTokens) return (f.Key, null, false);

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
            return (best.Key, best, false);
        }

        // Two rewordings of the same thing arriving in ONE reply would otherwise both
        // send, since neither is in the ledger yet.
        foreach (var (key, used) in usedThisRun)
            if (TopicSignature.Similarity(tokens, used) >= TopicSignature.SameTopicThreshold)
            {
                logger.LogInformation("{Source}: '{New}' duplicates '{Key}' within this run — dropping.", source, f.Key, key);
                return (key, null, true);
            }

        return (f.Key, null, false);
    }

    // Health problems say so up front — "San is degraded" and "your bill is overdue"
    // want very different reactions, and the icons alone don't carry that.
    private static string Header(string source) => source switch
    {
        "audit" => "🔎 System audit",
        "email" => "📬 Email triage",
        "health" => "🩺 San self-check",
        _ => "🔎 San",
    };

    private static string Icon(string severity) => severity switch
    {
        "critical" => "🔴",
        "high" => "🟠",
        "low" => "⚪",
        _ => "🟡",
    };
}
