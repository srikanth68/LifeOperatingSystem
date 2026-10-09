using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// One row per day, as the forecast and the self-check see a person.
//
// Two places hold the same facts. The typed tables (readiness, sleep, activity) are what
// the Oura sync and the phone's live push write. Observations are what everything else
// reads, and the Apple Health export file writes ONLY there. The forecast used to read
// the typed tables alone, so a person whose history came from an Apple Health export got
// a forecast built on nothing, with no sign that anything was missing.
//
// The rule: a typed value wins when there is one, and Observations fill only the holes.
// Typed-first keeps every existing Oura user's numbers exactly as they were (the Oura
// sync also projects into Observations, so taking both would count a night twice), and
// "fill the holes only" means this can add days to a history but never change one.
//
// Pure: given the same rows it returns the same days, which is what makes it testable.
public static class DayRowBuilder
{
    public static List<Prediction.DayRow> Build(
        IReadOnlyList<DailyReadiness> readiness,
        IReadOnlyList<DailyActivity> activity,
        IReadOnlyList<SleepSession> sleep,
        IReadOnlyList<Observation> observations,
        DateOnly from, DateOnly to)
    {
        var readinessByDay = readiness.GroupBy(r => r.Day).ToDictionary(g => g.Key, g => g.Last());
        var activityByDay = activity.GroupBy(a => a.Day).ToDictionary(g => g.Key, g => g.Last());
        var sleepByDay = sleep.GroupBy(x => x.Day)
            .ToDictionary(g => g.Key, g => SleepNights.Main(g));

        var observed = Index(observations);

        var rows = new List<Prediction.DayRow>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            readinessByDay.TryGetValue(day, out var r);
            activityByDay.TryGetValue(day, out var a);
            sleepByDay.TryGetValue(day, out var night);

            // A day with nothing at all is still a row. Dropping it would close the gap
            // and let a fortnight without the ring look like a continuous fortnight.
            rows.Add(new Prediction.DayRow(
                day,
                Readiness: r?.Score ?? Observed(observed, day, MetricKeys.ReadinessScore),
                RestingHr: night?.LowestHr ?? Observed(observed, day, MetricKeys.RestingHeartRate),
                Hrv: night?.AvgHrv ?? Observed(observed, day, MetricKeys.HrvRmssd),
                SleepMinutes: night?.TotalSleepMinutes ?? Observed(observed, day, MetricKeys.TotalSleepMinutes),
                ActiveCalories: a?.ActiveCalories ?? Observed(observed, day, MetricKeys.ActiveCalories)));
        }

        return rows;
    }

    private static readonly HashSet<string> Used =
    [
        MetricKeys.ReadinessScore, MetricKeys.RestingHeartRate, MetricKeys.HrvRmssd,
        MetricKeys.TotalSleepMinutes, MetricKeys.ActiveCalories,
    ];

    private static Dictionary<(DateOnly, string), List<Observation>> Index(IReadOnlyList<Observation> observations) =>
        observations
            .Where(o => Used.Contains(o.Metric))
            .GroupBy(o => (o.ObservedDateLocal, o.Metric))
            .ToDictionary(g => g.Key, g => g.ToList());

    private static bool Cumulative(string metric) =>
        metric is MetricKeys.TotalSleepMinutes or MetricKeys.ActiveCalories;

    // One source per day and metric, never a blend. Two devices reporting the same
    // night disagree, and averaging them describes neither. Oura is preferred because
    // it is the measurement the typed tables already carry, so a day filled from here
    // is consistent with the days around it; then the rest in a fixed order.
    private static double? Observed(Dictionary<(DateOnly, string), List<Observation>> index, DateOnly day, string metric)
    {
        if (!index.TryGetValue((day, metric), out var rows) || rows.Count == 0) return null;

        var source = rows.Select(o => o.Source)
            .Distinct()
            .OrderBy(s => s == "oura" ? 0 : s == "apple_health" ? 1 : 2)
            .ThenBy(s => s, StringComparer.Ordinal)
            .First();

        var values = rows.Where(o => o.Source == source).Select(o => o.Value).ToList();

        // A cumulative total (minutes slept, calories) re-entered is the same total, so
        // take the largest rather than adding duplicates together. A rate is averaged.
        return Cumulative(metric) ? values.Max() : values.Average();
    }
}
