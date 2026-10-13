using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

public record WindowMetric(
    string Metric,
    string Label,
    string? Unit,
    double? Median,
    double? MeanZ,             // the person's own z-scores, averaged across the window
    double Coverage,           // days with data / window days
    bool Summarised,
    string? Refusal);

public record LabWindow(
    int Days,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> Analytes,
    IReadOnlyList<WindowMetric> Metrics,
    double? SleepDebtAtDraw,
    double? MeanLoadRatio,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Interventions);

public record LabDrawContext(
    Guid PanelId,
    DateOnly DrawnOn,
    IReadOnlyList<LabWindow> Windows,
    Guid? PreviousPanelId,
    DateOnly? PreviousDrawnOn,
    IReadOnlyList<LabWindow> PreviousWindows,
    IReadOnlyList<string> Sentences);

// What the daily data looked like before a blood draw (Vitara ADR 0020).
//
// Labs arrive about twice a year. Correlating four draws against daily data would be a
// fit to four points, so the honest form is annotation: each draw beside the window of
// daily life that analyte actually reflects, and beside the previous draw's window.
// Co-occurrence only, in the wording too -- "in the 30 days before this draw", never
// "because" or "led to".
//
// Pure. Computed on request; nothing is stored.
public static class LabContext
{
    // How far back an analyte "sees". Physiology, not tuning, so constants, not settings.
    // HbA1c reflects about three months of glucose; lipids respond within weeks;
    // inflammation markers within days.
    public static int WindowFor(string analyte) => analyte switch
    {
        MetricKeys.Hba1c => 90,
        MetricKeys.TotalCholesterol or MetricKeys.Ldl or MetricKeys.Hdl or MetricKeys.Triglycerides
            or MetricKeys.NonHdl or MetricKeys.ApoB => 30,
        MetricKeys.Crp or MetricKeys.HsCrp => 14,
        _ => 30,
    };

    // Below this share of days with data, a metric is not summarised for the window.
    public const double MinCoverage = 0.5;

    // The daily measures a draw is read against: the ones a person can actually change
    // between draws, and that the ring measures well.
    public static readonly string[] DailyMetrics =
    [
        MetricKeys.Steps,
        MetricKeys.ActiveCalories,
        MetricKeys.TotalSleepMinutes,
        MetricKeys.RestingHeartRate,
        MetricKeys.HrvRmssd,
        MetricKeys.AlcoholUnits,
    ];

    // Words that turn co-occurrence into cause. Tested against every sentence produced.
    public static readonly string[] CausalWords =
        ["because", "led to", "caused", "causes", "due to", "resulted in", "drove", "thanks to", "as a result", "so your"];

    public static LabDrawContext? ForPanel(
        Guid panelId,
        IReadOnlyList<LabPanel> panels,
        IReadOnlyList<Observation> observations,
        IReadOnlyList<DerivedMetric> derived,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Intervention> interventions)
    {
        var panel = panels.FirstOrDefault(p => p.Id == panelId);
        if (panel is null) return null;

        var analytes = observations.Where(o => o.LabPanelId == panelId).Select(o => o.Metric).Distinct().ToList();
        var windows = Windows(panel.DrawnOnLocal, analytes, observations, derived, findings, interventions);

        // The previous draw that measured any of the same analytes.
        var previous = panels
            .Where(p => p.DrawnOnLocal < panel.DrawnOnLocal
                     && observations.Any(o => o.LabPanelId == p.Id && analytes.Contains(o.Metric)))
            .OrderByDescending(p => p.DrawnOnLocal)
            .FirstOrDefault();

        var previousWindows = previous is null
            ? []
            : Windows(previous.DrawnOnLocal, analytes, observations, derived, findings, interventions);

        return new LabDrawContext(panel.Id, panel.DrawnOnLocal, windows, previous?.Id, previous?.DrawnOnLocal,
            previousWindows, Sentences(windows, previousWindows));
    }

    // One window per distinct length the panel's analytes need: an HbA1c and an LDL drawn
    // together are read against three months and one month respectively.
    public static List<LabWindow> Windows(
        DateOnly drawnOn,
        IReadOnlyList<string> analytes,
        IReadOnlyList<Observation> observations,
        IReadOnlyList<DerivedMetric> derived,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Intervention> interventions)
    {
        return analytes
            .GroupBy(WindowFor)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var days = g.Key;
                var to = drawnOn.AddDays(-1);              // the draw day itself is not "before"
                var from = drawnOn.AddDays(-days);

                var metrics = DailyMetrics.Select(m => Summarise(m, from, to, days, observations, derived)).ToList();

                var debt = derived
                    .Where(d => d.Metric == MetricCatalogue.SleepDebtMinutes && d.ObservedDateLocal <= drawnOn)
                    .OrderByDescending(d => d.ObservedDateLocal)
                    .FirstOrDefault(d => d.ObservedDateLocal > drawnOn.AddDays(-3));

                var load = derived
                    .Where(d => d.Metric == MetricCatalogue.AcwrActiveCalories && d.ObservedDateLocal >= from && d.ObservedDateLocal <= to)
                    .Select(d => d.Value)
                    .ToList();

                var during = findings
                    .Where(f => f.FirstDetectedLocal <= to && (f.ResolvedLocal ?? f.LastDetectedLocal) >= from)
                    .Select(f => f.Summary)
                    .Distinct()
                    .ToList();

                var running = interventions
                    .Where(i => i.StartedOnLocal <= to && (i.EndedOnLocal is null || i.EndedOnLocal >= from))
                    .Select(i => $"{i.Name} (since {i.StartedOnLocal:d MMM yyyy})")
                    .ToList();

                return new LabWindow(days, from, to, g.OrderBy(a => a).ToList(), metrics,
                    debt is null ? null : Math.Round(debt.Value),
                    load.Count == 0 ? null : Math.Round(load.Average(), 2),
                    during, running);
            })
            .ToList();
    }

    private static WindowMetric Summarise(
        string metric, DateOnly from, DateOnly to, int days,
        IReadOnlyList<Observation> observations, IReadOnlyList<DerivedMetric> derived)
    {
        var info = MetricCatalogue.Find(metric);
        var label = info?.Label ?? metric;

        var daily = observations
            .Where(o => o.Metric == metric && o.ObservedDateLocal >= from && o.ObservedDateLocal <= to)
            .GroupBy(o => o.ObservedDateLocal)
            .Select(g => g.Average(o => o.Value))
            .ToList();

        var coverage = Math.Round(daily.Count / (double)days, 2);

        if (coverage < MinCoverage)
            return new WindowMetric(metric, label, info?.Unit, null, null, coverage, false,
                daily.Count == 0
                    ? $"No {Lower(label)} recorded before this draw."
                    : $"Not enough data before this draw: {daily.Count} of {days} days.");

        var z = derived
            .Where(d => d.Metric == $"{metric}_z" && d.ObservedDateLocal >= from && d.ObservedDateLocal <= to)
            .Select(d => d.Value)
            .ToList();

        return new WindowMetric(metric, label, info?.Unit,
            Math.Round(Statistics.Median(daily), info?.Decimals ?? 0),
            z.Count == 0 ? null : Math.Round(z.Average(), 2),
            coverage, true, null);
    }

    // Plain sentences, side by side with the previous draw. Co-occurrence only.
    private static List<string> Sentences(IReadOnlyList<LabWindow> windows, IReadOnlyList<LabWindow> previous)
    {
        var sentences = new List<string>();

        foreach (var w in windows)
        {
            var before = previous.FirstOrDefault(p => p.Days == w.Days);

            foreach (var m in w.Metrics.Where(m => m.Summarised))
            {
                var now = Format(m.Median!.Value, m.Unit);
                var then = before?.Metrics.FirstOrDefault(p => p.Metric == m.Metric && p.Summarised);

                // "Steps were", "time asleep was".
                var verb = m.Label.EndsWith('s') ? "were" : "was";
                sentences.Add(then is null
                    ? $"In the {w.Days} days before this draw your median {Lower(m.Label)} {verb} {now}."
                    : $"In the {w.Days} days before this draw your median {Lower(m.Label)} {verb} {now}, " +
                      $"{Compare(m.Median.Value, then.Median!.Value)} {Format(then.Median.Value, m.Unit)} before the previous one.");
            }
        }

        return sentences;
    }

    private static string Compare(double now, double then) =>
        Math.Abs(now - then) < 1e-9 ? "the same as" : now > then ? "up from" : "down from";

    private static string Format(double v, string? unit)
    {
        var number = Math.Abs(v) >= 1000 ? v.ToString("#,0") : Math.Abs(v) >= 10 ? v.ToString("0") : v.ToString("0.#");
        return unit is null or "" or "steps" ? number : $"{number} {unit}";
    }

    private static string Lower(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;
}
