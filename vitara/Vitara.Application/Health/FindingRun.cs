using System.Text.Json;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Application.Health;

// Everything a detection pass reads. No database, no clock.
public record FindingRunInputs(
    IReadOnlyList<Observation> Observations,
    IReadOnlyList<Baseline> Baselines,
    IReadOnlyList<DerivedMetric> Derived,
    DateOnly AsOf);

// Running every detector over one day's worth of computed state.
//
// The detectors were written first and left unwired, which meant the statistics were
// correct and the system said nothing. This is the part that makes them speak.
//
// Pure and synchronous, like the projector and the calculator it sits next to: given
// the same observations, baselines and derived values it produces the same findings.
// That is what lets a test state a scenario and assert on the output, and what makes
// re-running a pass safe.
public static class FindingRun
{
    // Which metrics are allowed to raise a deviation finding.
    //
    // NOT every metric with a baseline. Two-sigma sustained over two days is roughly a
    // one-in-a-thousand day per metric, which sounds negligible until it is multiplied
    // by thirty metrics and three hundred and sixty-five days. Worse, these metrics are
    // heavily correlated -- deep, REM and total sleep move together, so one poor week
    // arrives as four separate findings saying the same thing.
    //
    // So this is a curated list of the metrics where "unusually high or low for you"
    // is independently worth a sentence. The others are still measured, still
    // baselined, still visible on request; they just do not initiate a conversation.
    private static readonly string[] DeviationMetrics =
    [
        MetricKeys.RestingHeartRate,
        MetricKeys.HrvRmssd,
        MetricKeys.SkinTempDeviation,
        MetricKeys.TotalSleepMinutes,
        MetricKeys.ReadinessScore,
        MetricKeys.BreathingRate,
        MetricKeys.Spo2Average,
        MetricKeys.SystolicBp,
        MetricKeys.WeightKg,
        MetricKeys.Glucose,
    ];

    // The three the illness detector reads. When it fires, their individual deviation
    // findings are suppressed: "resting HR is high, HRV is low, temperature is up, and
    // you may be getting ill" is four notifications about one thing.
    private static readonly string[] IllnessComponents =
    [
        MetricKeys.RestingHeartRate,
        MetricKeys.HrvRmssd,
        MetricKeys.SkinTempDeviation,
    ];

    // Slow movers, where a trend is meaningful and a day-to-day reading is not.
    private static readonly string[] SlowMetrics =
    [
        MetricKeys.RestingHeartRate,
        MetricKeys.HrvRmssd,
        MetricKeys.WeightKg,
        MetricKeys.SystolicBp,
    ];

    // Staleness applies to the dense tier only. A weight reading is entered by hand
    // every few days and a lab twice a year -- reporting either as "missing" would be
    // reporting the schedule, not a problem. A ring that stopped syncing is a problem.
    private static readonly string[] DailyExpected =
    [
        MetricKeys.RestingHeartRate,
        MetricKeys.TotalSleepMinutes,
        MetricKeys.ReadinessScore,
    ];

    private const int DriftWindowDays = 90;

    public static List<Finding> Detect(FindingRunInputs input) =>
        Detect(input, HealthThresholdSet.FromConfiguration());

    public static List<Finding> Detect(FindingRunInputs input, HealthThresholdSet illness)
    {
        var asOf = input.AsOf;
        var findings = new List<Finding>();

        // ── Early illness, first, because it can suppress its own components ──────
        var illnessFinding = FindingDetectors.EarlyIllness(
            BuildVitals(input.Derived), illness, HealthThresholds.IllnessSustainedDays);

        if (illnessFinding is not null) findings.Add(illnessFinding);

        // ── Deviation ─────────────────────────────────────────────────────────────
        var valid = input.Baselines
            .Where(b => b.IsValid)
            .Select(b => b.Metric)
            .ToHashSet();

        foreach (var metric in DeviationMetrics)
        {
            // A z-score against an unproven baseline is a confident number built on
            // four readings, and the confident one is the one that gets acted on.
            if (!valid.Contains(metric)) continue;

            // Already said, better, by the illness finding.
            if (illnessFinding is not null && IllnessComponents.Contains(metric)) continue;

            var series = ZSeries(input.Derived, metric);
            if (series.Count == 0) continue;

            var found = FindingDetectors.Deviation(
                metric, series, HealthThresholds.DeviationZ, HealthThresholds.DeviationSustainedDays);

            if (found is not null) findings.Add(found);
        }

        // ── Regime change ─────────────────────────────────────────────────────────
        // Re-detected here rather than read off the baseline row. The baseline stores
        // only where the current regime starts; the size and direction of the step are
        // what a finding has to say, and those are not persisted.
        foreach (var metric in SlowMetrics)
        {
            var series = input.Observations
                .Where(o => o.Metric == metric)
                .GroupBy(o => o.ObservedDateLocal)
                .Select(g => (Day: g.Key, Value: g.Average(o => o.Value)))
                .OrderBy(p => p.Day)
                .ToList();

            var change = RegimeDetector.Detect(
                series, HealthThresholds.RegimeShiftZ, HealthThresholds.RegimeDwellDays);

            if (change is null) continue;

            // Only if it is recent. An older step has already been absorbed into how
            // things are, and announcing it now would be announcing history.
            if (change.ChangePointLocal < asOf.AddDays(-HealthThresholds.RegimeDwellDays * 3)) continue;

            findings.Add(FindingDetectors.FromRegimeChange(metric, change, Attribute(input, change), asOf));
        }

        // ── Strain and sleep debt ─────────────────────────────────────────────────
        var acwr = Latest(input.Derived, "acwr_active_calories", asOf);
        var strain = FindingDetectors.Strain(
            acwr?.Value, HealthThresholds.AcwrLow, HealthThresholds.AcwrHigh, asOf);
        if (strain is not null) findings.Add(strain);

        if (Latest(input.Derived, "sleep_debt_minutes", asOf) is { } debt)
        {
            // The basis travels with the number: "four hours short" means something
            // different when the need behind it was inferred rather than stated.
            var basis = ReadString(debt.InputsJson, "basis") ?? "estimated";
            var found = FindingDetectors.SleepDebtFinding(
                debt.Value, HealthThresholds.SleepDebtThresholdMinutes, basis, asOf);

            if (found is not null) findings.Add(found);
        }

        // ── Drift ─────────────────────────────────────────────────────────────────
        foreach (var metric in SlowMetrics)
        {
            var points = input.Observations
                .Where(o => o.Metric == metric && o.ObservedDateLocal > asOf.AddDays(-DriftWindowDays))
                .Select(o => (o.ObservedDateLocal.DayNumber, o.Value))
                .ToList();

            if (Statistics.Trend(points) is not { } trend) continue;
            if (points.Count < HealthThresholds.DriftMinDays) continue;

            var found = FindingDetectors.Drift(metric, trend, DriftWindowDays, asOf);
            if (found is not null) findings.Add(found);
        }

        // ── Staleness ─────────────────────────────────────────────────────────────
        foreach (var metric in DailyExpected)
        {
            var lastSeen = input.Observations
                .Where(o => o.Metric == metric)
                .Select(o => (DateOnly?)o.ObservedDateLocal)
                .Max();

            // Never recorded is not the same as stopped recording. A metric the user
            // has never had is not a gap in their data, and saying so on day one would
            // be the system complaining about its own emptiness.
            if (lastSeen is null) continue;

            var found = FindingDetectors.Staleness(metric, lastSeen, asOf, HealthThresholds.StalenessDays);
            if (found is not null) findings.Add(found);
        }

        return findings;
    }

    // The three z-scores the illness signal reads, aligned by day. A day missing any
    // of them still counts -- the detector requires two of three, so an absent skin
    // temperature does not silence a clear resting-HR and HRV signal.
    private static List<DailyVitals> BuildVitals(IReadOnlyList<DerivedMetric> derived)
    {
        var rhr = ZByDay(derived, MetricKeys.RestingHeartRate);
        var hrv = ZByDay(derived, MetricKeys.HrvRmssd);
        var temp = ZByDay(derived, MetricKeys.SkinTempDeviation);

        return rhr.Keys.Union(hrv.Keys).Union(temp.Keys)
            .OrderBy(d => d)
            .Select(d => new DailyVitals(
                d,
                rhr.TryGetValue(d, out var r) ? r : null,
                hrv.TryGetValue(d, out var h) ? h : null,
                temp.TryGetValue(d, out var t) ? t : null))
            .ToList();
    }

    private static Dictionary<DateOnly, double> ZByDay(IReadOnlyList<DerivedMetric> derived, string metric) =>
        derived.Where(d => d.Metric == $"{metric}_z")
            .GroupBy(d => d.ObservedDateLocal)
            .ToDictionary(g => g.Key, g => g.Last().Value);

    private static List<(DateOnly Day, double Z)> ZSeries(IReadOnlyList<DerivedMetric> derived, string metric) =>
        derived.Where(d => d.Metric == $"{metric}_z")
            .OrderBy(d => d.ObservedDateLocal)
            .Select(d => (d.ObservedDateLocal, d.Value))
            .ToList();

    private static DerivedMetric? Latest(IReadOnlyList<DerivedMetric> derived, string metric, DateOnly asOf) =>
        derived.FirstOrDefault(d => d.Metric == metric && d.ObservedDateLocal == asOf);

    // What else changed around the same time, offered as a possible explanation and
    // never as a cause. A device swap on the day a metric steps is worth knowing about;
    // asserting it did it is a claim the data cannot support.
    private static string? Attribute(FindingRunInputs input, RegimeChange change)
    {
        var window = 7;

        var travel = input.Observations.Count > 0 &&
            input.Baselines.Any(b => b.RegimeStartLocal is { } s
                && Math.Abs(s.DayNumber - change.ChangePointLocal.DayNumber) <= window);

        return travel ? "another metric shifted around the same time" : null;
    }

    private static string? ReadString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            // A malformed inputs blob must not take down a detection pass. The number
            // it accompanies is still good; only its provenance is lost.
            return null;
        }
    }
}
