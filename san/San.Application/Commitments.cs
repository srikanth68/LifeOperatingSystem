namespace San.Application;

// One thing the user said they would do, from wherever it was recorded.
public record Commitment(
    string Key,          // stable across runs — the ledger dedupes on it
    string Title,
    string Source,       // "northstar" | "aasthi"
    DateOnly? DueOn,
    DateTime CreatedAtUtc,
    int Priority,        // 1 = highest, 5 = lowest (NorthStar's convention)
    string? Context);    // property address, category — whatever locates it

// Decides which open commitments have gone stale enough to be worth saying out loud.
//
// San could already RECORD "you said you'd call the insurance company" and then never
// mention it again: nothing read ActionItem except on request. Recording an obligation
// and never raising it is arguably worse than not recording it, because it creates the
// impression something is being watched.
//
// Deterministic on purpose. "This was due five days ago" is arithmetic, not judgment,
// and handing arithmetic to gemma-4-E4B is how you get confident wrong numbers. What
// the model is good at — deciding what MATTERS — is not actually the question here;
// the user already decided it mattered when they wrote it down.
//
// The real design problem is cadence, not detection. A system that chases you becomes
// one you resent and then ignore, which costs the credibility to be heard on the thing
// that did matter. So this only assigns a severity, and everything about how often it
// may speak is left to the ledger's cooldowns and quiet hours, which already work.
public static class Commitments
{
    // Below this a commitment is simply young. Nagging about something recorded
    // yesterday is how the whole channel gets muted.
    public static readonly int QuietDays =
        int.TryParse(Environment.GetEnvironmentVariable("COMMITMENT_QUIET_DAYS"), out var d) && d >= 0 ? d : 7;

    public static AgentFinding? Evaluate(Commitment c, DateOnly today, DateTime nowUtc)
    {
        var where = string.IsNullOrWhiteSpace(c.Context) ? "" : $" ({c.Context})";

        if (c.DueOn is { } due)
        {
            var daysLate = today.DayNumber - due.DayNumber;

            if (daysLate > 0)
            {
                // Severity climbs with lateness, and the ledger suspends its backoff
                // near a deadline — so something genuinely overdue keeps a steady
                // cadence instead of fading out precisely when it matters most.
                var severity = daysLate switch
                {
                    >= 14 => "high",
                    >= 3 => "medium",
                    _ => "low",
                };
                var howLate = daysLate == 1 ? "yesterday" : $"{daysLate} days ago";
                return new AgentFinding(c.Key, severity,
                    $"Still open: \"{c.Title}\"{where} — was due {howLate}.",
                    due.ToDateTime(TimeOnly.MinValue));
            }

            var daysUntil = due.DayNumber - today.DayNumber;
            if (daysUntil <= 2)
            {
                var when = daysUntil == 0 ? "today" : daysUntil == 1 ? "tomorrow" : $"in {daysUntil} days";
                return new AgentFinding(c.Key, "medium",
                    $"Due {when}: \"{c.Title}\"{where}.",
                    due.ToDateTime(TimeOnly.MinValue));
            }

            return null;   // has a date, not near it — nothing to say
        }

        // No deadline. These are the ones that quietly rot: nothing makes them
        // overdue, so without this they are never mentioned again.
        var age = (nowUtc - c.CreatedAtUtc).TotalDays;
        if (age < QuietDays) return null;

        // High-priority items get chased sooner than the rest — priority 1-2 is the
        // user having said "this one matters" at the time they wrote it down.
        var threshold = c.Priority <= 2 ? QuietDays : QuietDays * 3;
        if (age < threshold) return null;

        return new AgentFinding(c.Key, c.Priority <= 2 ? "medium" : "low",
            $"Still open after {(int)age} days: \"{c.Title}\"{where}.", null);
    }

    // Newest-first would surface whatever was written last; oldest and most overdue
    // first surfaces what has actually been neglected.
    public static IEnumerable<AgentFinding> EvaluateAll(
        IEnumerable<Commitment> commitments, DateOnly today, DateTime nowUtc, int max = 5) =>
        commitments
            .Select(c => (Commitment: c, Finding: Evaluate(c, today, nowUtc)))
            .Where(x => x.Finding is not null)
            .OrderBy(x => x.Commitment.DueOn ?? DateOnly.MaxValue)
            .ThenBy(x => x.Commitment.Priority)
            .ThenBy(x => x.Commitment.CreatedAtUtc)
            .Take(max)
            .Select(x => x.Finding!);
}
