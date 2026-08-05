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
        var due = new List<AgentFinding>();

        foreach (var f in findings)
        {
            var entry = await repo.GetLedgerEntryAsync(f.Key);
            if (entry is not null &&
                !NotifyPolicy.ShouldNotify(f.Severity, entry.NotifyCount, entry.LastNotifiedAt, f.DueOn, now))
            {
                var wait = NotifyPolicy.Cooldown(f.Severity, entry.NotifyCount, f.DueOn, now) - (now - entry.LastNotifiedAt);
                logger.LogInformation("{Source}: suppressing '{Key}' ({Severity}, told {Count}x) — {Hours:F1}h of cooldown left.",
                    source, f.Key, f.Severity, entry.NotifyCount, wait.TotalHours);
                continue;
            }
            due.Add(f);
        }

        if (due.Count == 0)
        {
            logger.LogInformation("{Source}: {Total} finding(s), all within cooldown — staying quiet.", source, findings.Count);
            return 0;
        }

        var body = string.Join("\n", due.Select(f => $"{Icon(f.Severity)} {f.Message}"));
        var header = source == "audit" ? "🔎 System audit" : "📬 Email triage";
        if (telegram.IsConfigured)
            await telegram.SendAsync($"{header}:\n{body}", ct);

        foreach (var f in due)
            await repo.RecordNotificationAsync(new NotificationLedgerEntry
            {
                Key = f.Key,
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

    private static string Icon(string severity) => severity switch
    {
        "critical" => "🔴",
        "high" => "🟠",
        "low" => "⚪",
        _ => "🟡",
    };
}
