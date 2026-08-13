using Vitara.Domain.Entities;

namespace Vitara.Application;

// One finding: how readiness the morning AFTER a kind of workout compares with the
// morning after a rest day.
public record WorkoutImpactFinding(
    string Dimension,      // "activity" | "intensity"
    string Group,          // e.g. "weight_training", "hard"
    int Samples,
    double AvgAfter,
    double Delta);         // AvgAfter - Baseline; negative means it costs you recovery

public record WorkoutImpactReport(
    double Baseline,
    int BaselineDays,
    IReadOnlyList<WorkoutImpactFinding> Findings,
    string? Note);

// Connects the two halves of Vitara that have never spoken to each other: workouts get
// logged, Oura scores recovery, and nothing relates them. "Your readiness drops 12
// points the morning after hard sessions" is a claim only this dataset can make, and
// it's the sort of thing a person genuinely changes behaviour over.
//
// Deliberately conservative, for the reason InsightGrounding exists — a confident wrong
// number about your own body is worse than no number. Three guards:
//
//   1. A minimum sample count per group, so one bad night can't become a "pattern".
//   2. A minimum effect size, because readiness moves a few points on its own and
//      reporting that as a training effect is noise with a decimal point.
//   3. A baseline of rest-day mornings. Comparing hard sessions against your overall
//      average would be measuring them partly against themselves.
//
// NOT grouped by time of day, though "evening sessions cost you more" is the most
// interesting question here. Workout.StartTime is parsed with DateTime.Parse from an
// offset-bearing Oura string, so what it means depends on the container's TZ — and a
// wrongly-bucketed evening is an insight that is confidently backwards. Needs the
// timestamps to carry their offset before that question can be asked honestly.
public static class WorkoutImpact
{
    // Three mornings is thin, but a fortnight of data can't offer more and a person can
    // weigh "3 samples" themselves. It's reported alongside every finding for exactly
    // that reason.
    public const int MinSamples = 3;

    // Oura readiness drifts a few points day to day with nothing behind it.
    public const double MinEffect = 3.0;

    public static WorkoutImpactReport Analyze(
        IEnumerable<Workout> workouts,
        IEnumerable<DailyReadiness> readiness)
    {
        var scoreByDay = readiness
            .Where(r => r.Score.HasValue)
            .GroupBy(r => r.Day)
            .ToDictionary(g => g.Key, g => (double)g.First().Score!.Value);

        var workoutsByDay = workouts
            .GroupBy(w => w.Day)
            .ToDictionary(g => g.Key, g => g.ToList());

        // The morning after. Keyed by the day that was TRAINED, valued by how the next
        // day scored — every comparison below is "the day after X" versus "the day
        // after nothing".
        var morningAfter = new Dictionary<DateOnly, double>();
        foreach (var (day, _) in workoutsByDay)
            if (scoreByDay.TryGetValue(day.AddDays(1), out var next))
                morningAfter[day] = next;

        // Baseline: mornings whose previous day held no workout at all.
        var restMornings = scoreByDay
            .Where(kv => !workoutsByDay.ContainsKey(kv.Key.AddDays(-1)))
            .Select(kv => kv.Value)
            .ToList();

        if (restMornings.Count < MinSamples)
            return new WorkoutImpactReport(0, restMornings.Count, [],
                "Not enough rest days yet to know what your normal recovery looks like.");

        var baseline = restMornings.Average();
        var findings = new List<WorkoutImpactFinding>();

        findings.AddRange(Group("activity", morningAfter, workoutsByDay, w => Normalise(w.Activity), baseline));
        findings.AddRange(Group("intensity", morningAfter, workoutsByDay, w => Normalise(w.Intensity), baseline));

        // Largest effect first — the point is what to change, and the biggest number is
        // the most likely answer. Ties broken by sample count, so better-evidenced
        // findings win.
        var ordered = findings
            .OrderByDescending(f => Math.Abs(f.Delta))
            .ThenByDescending(f => f.Samples)
            .ToList();

        return new WorkoutImpactReport(Math.Round(baseline, 1), restMornings.Count, ordered,
            ordered.Count == 0 ? "No workout type has moved your next-day readiness noticeably yet." : null);
    }

    private static IEnumerable<WorkoutImpactFinding> Group(
        string dimension,
        Dictionary<DateOnly, double> morningAfter,
        Dictionary<DateOnly, List<Workout>> workoutsByDay,
        Func<Workout, string?> key,
        double baseline)
    {
        // A day counts once per group even if it held two sessions of the same kind —
        // otherwise a double day would weight that morning twice.
        var byGroup = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (day, dayWorkouts) in workoutsByDay)
        {
            if (!morningAfter.TryGetValue(day, out var score)) continue;
            foreach (var group in dayWorkouts.Select(key).Where(g => g is not null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byGroup.TryGetValue(group!, out var list)) byGroup[group!] = list = [];
                list.Add(score);
            }
        }

        foreach (var (group, scores) in byGroup)
        {
            if (scores.Count < MinSamples) continue;
            var avg = scores.Average();
            var delta = avg - baseline;
            if (Math.Abs(delta) < MinEffect) continue;
            yield return new WorkoutImpactFinding(dimension, group, scores.Count, Math.Round(avg, 1), Math.Round(delta, 1));
        }
    }

    private static string? Normalise(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();
}
