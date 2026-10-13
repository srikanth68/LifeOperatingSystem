using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

public record DomainMetric(
    string Metric,
    string Label,
    string? Unit,
    double? Latest,
    int? DaysAgo,
    string Baseline,          // ready | learning | none
    string Trajectory,
    string TrajectoryReason,
    string? Grade);

public record DomainGaps(
    IReadOnlyList<string> Unmeasured,
    IReadOnlyList<string> Stale,
    IReadOnlyList<string> MissingKeyTests);

public record DomainSummary(
    string Key,
    string Label,
    string Question,
    IReadOnlyList<DomainMetric> Metrics,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> Patterns,
    DomainGaps Gaps,
    string Status,
    IReadOnlyList<string> ReadWith);

// "How am I doing?", per domain, without a score (Vitara ADR 0019).
//
// The obvious design -- and the one competitors ship -- is a number per domain or one for
// everything. A score hides which measure moved, weights a lipid panel and a vendor
// estimate together, and gives the reader a number nobody can audit. So the summary is
// counts and one sentence built in code, and the gaps: the part most worth acting on,
// because a gap names the test to ask for.
//
// Pure. Everything already computed goes in; nothing is read from anywhere.
public static class DomainView
{
    public static List<DomainSummary> Build(
        IReadOnlyList<TrajectoryResult> trajectories,
        IReadOnlyDictionary<string, (DateOnly Day, double Value)> latest,
        IReadOnlyDictionary<string, string> baselineStates,
        IReadOnlyList<Finding> activeFindings,
        IReadOnlyList<PatternResult> patterns,
        DateOnly asOf)
    {
        var byMetric = trajectories.ToDictionary(t => t.Metric);

        return Domains.All.Select(domain =>
        {
            var metrics = domain.Metrics.Select(key =>
            {
                var info = MetricCatalogue.Find(key);
                var t = byMetric.GetValueOrDefault(key);
                var has = latest.TryGetValue(key, out var l);
                return new DomainMetric(
                    key,
                    info?.Label ?? key,
                    info?.Unit,
                    has ? Math.Round(l.Value, (info?.Decimals ?? 1) + 1) : null,
                    has ? asOf.DayNumber - l.Day.DayNumber : null,
                    baselineStates.GetValueOrDefault(key, "none"),
                    t?.State ?? TrajectoryStates.InsufficientData,
                    t?.Reason ?? "Never measured.",
                    Evidence.GradeFor(key)?.ToString());
            }).ToList();

            var counts = new Dictionary<string, int>
            {
                [TrajectoryStates.Improving] = metrics.Count(m => m.Trajectory == TrajectoryStates.Improving),
                [TrajectoryStates.Worsening] = metrics.Count(m => m.Trajectory == TrajectoryStates.Worsening),
                [TrajectoryStates.Stable] = metrics.Count(m => m.Trajectory == TrajectoryStates.Stable),
                [TrajectoryStates.Uncertain] = metrics.Count(m => m.Trajectory == TrajectoryStates.Uncertain),
                [TrajectoryStates.InsufficientData] = metrics.Count(m => m.Trajectory == TrajectoryStates.InsufficientData),
                // Moving, where no direction is better. Counted, never called good or bad.
                ["moving"] = metrics.Count(m => m.Trajectory is TrajectoryStates.Rising or TrajectoryStates.Falling),
            };

            var findings = activeFindings
                .Where(f => domain.Metrics.Contains(f.Metric))
                .GroupBy(f => f.Key)          // once per domain, however many metrics it touches
                .Select(g => g.First())
                .ToList();

            var touching = patterns
                .Where(p => p.Fires && p.Components.Any(c => domain.Metrics.Contains(c.Metric)))
                .Select(p => p.Title)
                .ToList();

            var unmeasured = metrics
                .Where(m => m.Latest is null)
                .Select(m => m.Label)
                .ToList();

            var stale = metrics
                .Where(m => m.DaysAgo is { } ago && ago > (MetricCatalogue.Find(m.Metric)?.StaleAfterDays ?? 3))
                .Select(m => $"{m.Label} ({m.DaysAgo} days ago)")
                .ToList();

            var missing = domain.KeyTests
                .Where(any => any.All(k => !latest.ContainsKey(k)))
                .Select(any => any.Length == 1
                    ? $"No {Lower(MetricCatalogue.Find(any[0])?.Label ?? any[0])} on record"
                    : $"None of: {string.Join(", ", any.Select(k => MetricCatalogue.Find(k)?.Label ?? k))}")
                .ToList();

            var readWith = (domain.Refers ?? []).Select(r => Domains.Find(r)?.Label ?? r).ToList();

            return new DomainSummary(
                domain.Key, domain.Label, domain.Question, metrics, counts, findings, touching,
                new DomainGaps(unmeasured, stale, missing),
                Status(metrics, counts, findings.Count, readWith),
                readWith);
        }).ToList();
    }

    // One sentence, built from the counts. Never a judgement of the domain as a whole:
    // it says what moved, which way, and how much is unknown.
    private static string Status(
        IReadOnlyList<DomainMetric> metrics, IReadOnlyDictionary<string, int> counts, int findings, IReadOnlyList<string> readWith)
    {
        var parts = new List<string>();

        void Add(string state, string words, bool name = false)
        {
            var n = counts[state];
            if (n == 0) return;
            var named = name
                ? $" ({string.Join(", ", metrics.Where(m => m.Trajectory == state).Select(m => Lower(m.Label)))})"
                : "";
            parts.Add($"{n} {words}{named}");
        }

        Add(TrajectoryStates.Improving, "improving");
        Add(TrajectoryStates.Worsening, "worsening", name: true);
        Add(TrajectoryStates.Stable, "stable");
        Add("moving", "moving with no better direction");
        Add(TrajectoryStates.Uncertain, "unclear");
        Add(TrajectoryStates.InsufficientData, "without enough data");

        var sentence = parts.Count == 0 ? "Nothing measured yet." : $"{Capital(string.Join(", ", parts))}.";

        if (findings > 0) sentence += $" {findings} active finding{(findings == 1 ? "" : "s")}.";
        if (readWith.Count > 0) sentence += $" Read with: {string.Join(", ", readWith)}.";
        return sentence;
    }

    private static string Lower(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
