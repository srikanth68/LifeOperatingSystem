using System.Text.Json;
using Microsoft.Extensions.Logging;
using San.Application;
using San.Application.Interfaces;

namespace San.Worker;

// San noticing that San is broken, and saying so through the same channel as
// everything else it notices.
//
// Deliberately model-free. "The MCP gateway is serving 10 tools instead of 41" is
// exactly the report you cannot ask a possibly-degraded San to write for you, so the
// findings are derived from the probe and handed straight to FindingDispatcher, where
// they pick up the keyed suppression, cooldowns, escalation and NorthStar recording
// that the model's findings already get.
public static class SelfCheck
{
    // Every `docker compose up -d` restarts all containers at once. A check landing in
    // that window sees seven unreachable modules and a dead MCP, none of which is real.
    // Requiring a problem to be seen TWICE before it can notify costs one interval of
    // detection latency and removes that entire class of false alarm — which matters
    // here more than usual, since notification spam has already had to be fixed twice.
    private const string PendingKey = "health.unconfirmed_problems";

    public static async Task<int> RunAsync(
        IHealthProbe probe,
        ISanRepository repo,
        ITelegramNotifier telegram,
        IModuleContextService moduleContext,
        ILogger logger,
        CancellationToken ct)
    {
        var report = await probe.RunAsync(ct);
        var seenNow = report.Problems.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var seenLast = await LoadPendingAsync(repo);

        // Persist BEFORE dispatching: if the send throws, the next run should still
        // treat these as already-seen rather than restarting the two-strike count.
        await SavePendingAsync(repo, seenNow);

        if (report.Problems.Count == 0)
        {
            logger.LogInformation("Self-check: all clear ({Ms}ms).", report.CheckedInMs);
            return 0;
        }

        var confirmed = report.Problems.Where(p => seenLast.Contains(p.Key)).ToList();
        var awaiting = report.Problems.Count - confirmed.Count;
        if (awaiting > 0)
            logger.LogInformation(
                "Self-check: {Awaiting} problem(s) seen for the first time — holding until the next run confirms them.",
                awaiting);

        if (confirmed.Count == 0) return 0;

        var findings = confirmed
            .Select(p => new AgentFinding(p.Key, p.Severity, p.Message, null))
            .ToList();

        logger.LogWarning("Self-check: {Count} confirmed problem(s) — {Keys}",
            findings.Count, string.Join(", ", confirmed.Select(p => p.Key)));

        return await FindingDispatcher.DispatchFindingsAsync(
            findings, "health", repo, telegram, moduleContext, logger, ct);
    }

    private static async Task<HashSet<string>> LoadPendingAsync(ISanRepository repo)
    {
        try
        {
            var raw = await repo.GetSettingAsync(PendingKey);
            if (string.IsNullOrWhiteSpace(raw)) return new HashSet<string>(StringComparer.Ordinal);
            return JsonSerializer.Deserialize<List<string>>(raw)?.ToHashSet(StringComparer.Ordinal)
                   ?? new HashSet<string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static Task SavePendingAsync(ISanRepository repo, HashSet<string> keys) =>
        repo.SetSettingAsync(PendingKey, JsonSerializer.Serialize(keys.OrderBy(k => k, StringComparer.Ordinal)));
}
