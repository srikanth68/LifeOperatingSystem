using System.Text.Json;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// Everything that can disqualify a reading from shaping what counts as normal.
public record BaselineInputs(
    IReadOnlyList<Observation> Observations,
    IReadOnlyList<ExcludedPeriod> Excluded,
    IReadOnlyList<TravelPeriod> Travel,
    IReadOnlyList<Device> Devices,
    // What was deliberately started or stopped. Only used to decide whether a step
    // change has an explanation; optional so callers that keep none still work.
    IReadOnlyList<Intervention>? Interventions = null);

// What normal looks like, and what was left out of deciding that.
//
// The exclusions are the part people skip, and they are why this is more than a mean.
// Two weeks of illness inside a rolling window raises the baseline, and the system
// then stops flagging exactly what it exists to flag -- while every number it reports
// still looks perfectly reasonable.
public static class BaselineCalculator
{
    // Metrics that cross-timezone travel corrupts rather than merely influences. Sleep
    // timing in a different timezone is not a worse night; it is a different question,
    // and pooling the two produces a baseline describing neither.
    private static readonly HashSet<string> CircadianMetrics =
    [
        MetricKeys.TotalSleepMinutes, MetricKeys.DeepSleepMinutes, MetricKeys.RemSleepMinutes,
        MetricKeys.SleepScore, MetricKeys.SleepEfficiency, MetricKeys.HrvRmssd,
        MetricKeys.RestingHeartRate, MetricKeys.ReadinessScore,
    ];

    public static Baseline Compute(
        string metric, string signature, DateOnly asOf, BaselineInputs inputs,
        int windowDays, int minN, double regimeShiftZ, int regimeDwellDays)
    {
        var from = asOf.AddDays(-windowDays);
        var dropped = new Dictionary<string, int>();

        var candidates = inputs.Observations
            .Where(o => o.Metric == metric
                     && o.BaselineSignature == signature
                     && o.ObservedDateLocal > from
                     && o.ObservedDateLocal <= asOf)
            .OrderBy(o => o.ObservedDateLocal)
            .ToList();

        // A reading whose metric needs context it does not have. Stored and shown; it
        // simply belongs to no bucket, and pooling it would corrupt whichever one it
        // landed in.
        var eligible = Drop(candidates, o => !o.EligibleForBaseline, dropped, "missing_context");

        // Illness, injury, a training block. Real data, deliberately not normal.
        eligible = Drop(eligible, o => inputs.Excluded.Any(e =>
            e.ExcludeFromBaseline && o.ObservedDateLocal >= e.StartLocal && o.ObservedDateLocal <= e.EndLocal),
            dropped, "excluded_period");

        if (CircadianMetrics.Contains(metric))
            eligible = Drop(eligible, o => inputs.Travel.Any(t =>
                o.ObservedDateLocal >= t.StartLocal && o.ObservedDateLocal <= t.EndLocal),
                dropped, "travel");

        // A device change is a discontinuity, not an event. A new ring or a firmware
        // update shifts absolute values, and a window spanning the swap describes two
        // different instruments averaged together -- while reporting the swap itself as
        // a health change.
        var latestDeviceStart = inputs.Devices
            .Where(d => d.ActiveFromLocal <= asOf)
            .Select(d => (DateOnly?)d.ActiveFromLocal)
            .DefaultIfEmpty(null)
            .Max();

        if (latestDeviceStart is { } deviceStart && deviceStart > from)
            eligible = Drop(eligible, o => o.ObservedDateLocal < deviceStart, dropped, "pre_device_change");

        // A sustained step to a new level resets the window rather than contaminating
        // it. Detected on what survives the exclusions above, so a fortnight of illness
        // is not mistaken for a new normal.
        //
        // But only when the step is explained or harmless -- see RegimeDecision. An
        // unexplained step the wrong way is left UNADOPTED: the baseline stays where it
        // was, so deviation keeps measuring against the person this metric used to
        // describe rather than quietly agreeing that worse is now normal.
        DateOnly? regimeStart = null;
        var series = eligible.Select(o => (o.ObservedDateLocal, o.Value)).ToList();
        if (RegimeDetector.Detect(series, regimeShiftZ, regimeDwellDays) is { } change)
        {
            var explanation = RegimeDecision.Explain(
                change.ChangePointLocal, inputs.Interventions, inputs.Excluded, inputs.Travel, inputs.Devices);

            if (RegimeDecision.ShouldAdopt(metric, change.Direction, explanation))
            {
                regimeStart = change.ChangePointLocal;
                eligible = Drop(eligible, o => o.ObservedDateLocal < change.ChangePointLocal, dropped, "before_regime_change");
            }
            else
            {
                // Counted like a drop so the baseline can still explain itself: the row
                // says a shift was seen and deliberately not taken as the new normal.
                dropped["unadopted_regime_shift"] = change.DaysHeld;
            }
        }

        var values = eligible.Select(o => o.Value).ToList();
        var kept = Statistics.WithoutOutliers(values);
        if (kept.Count < values.Count) dropped["outlier"] = values.Count - kept.Count;

        return new Baseline
        {
            Metric = metric,
            BaselineSignature = signature,
            ComputedOnLocal = asOf,
            WindowDays = windowDays,
            Mean = Statistics.Mean(kept),
            StdDev = Statistics.StdDev(kept),
            Median = Statistics.Median(kept),
            P25 = Statistics.Percentile(kept, 0.25),
            P75 = Statistics.Percentile(kept, 0.75),
            N = kept.Count,
            // Exposed as a flag rather than suppressed. A caller can then say "not
            // enough data yet", which is true and useful, instead of showing nothing or
            // showing a mean of four readings as though it meant something.
            IsValid = kept.Count >= minN,
            RegimeStartLocal = regimeStart,
            ExclusionsJson = dropped.Count == 0 ? null : JsonSerializer.Serialize(dropped),
        };
    }

    // Every drop is counted, so a baseline can explain itself. A window that lost a
    // third of its readings is saying something, and silently shrinking hides it.
    private static List<Observation> Drop(
        List<Observation> source, Func<Observation, bool> predicate, Dictionary<string, int> tally, string reason)
    {
        var kept = source.Where(o => !predicate(o)).ToList();
        var lost = source.Count - kept.Count;
        if (lost > 0) tally[reason] = tally.GetValueOrDefault(reason) + lost;
        return kept;
    }
}
