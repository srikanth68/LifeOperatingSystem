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
    IReadOnlyList<Intervention>? Interventions = null,
    // Steps already seen and not adopted, and the person's own word that a held level
    // is their normal now. Both outlive the window; see RegimeHold.
    IReadOnlyList<RegimeHold>? Holds = null,
    IReadOnlyList<RegimeAcceptance>? Acceptances = null);

// What a pass did to a hold. Returned rather than written: the calculator stays pure,
// and the worker is the one place that persists it.
public record HoldChange(string Kind, RegimeHold Hold)
{
    public const string Opened = "opened";
    public const string Released = "released";
}

public record BaselineResult(Baseline Baseline, HoldChange? Hold);

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
    internal static readonly HashSet<string> CircadianMetrics =
    [
        MetricKeys.TotalSleepMinutes, MetricKeys.DeepSleepMinutes, MetricKeys.RemSleepMinutes,
        MetricKeys.SleepScore, MetricKeys.SleepEfficiency, MetricKeys.HrvRmssd,
        MetricKeys.RestingHeartRate, MetricKeys.ReadinessScore,
        MetricKeys.BedtimeMinutes, MetricKeys.WakeMinutes, MetricCatalogue.BedtimeVariabilityMinutes,
    ];

    public static Baseline Compute(
        string metric, string signature, DateOnly asOf, BaselineInputs inputs,
        int windowDays, int minN, double regimeShiftZ, int regimeDwellDays) =>
        ComputeWithHold(metric, signature, asOf, inputs, windowDays, minN, regimeShiftZ, regimeDwellDays).Baseline;

    public static BaselineResult ComputeWithHold(
        string metric, string signature, DateOnly asOf, BaselineInputs inputs,
        int windowDays, int minN, double regimeShiftZ, int regimeDwellDays,
        int revertDays = 14, double revertFraction = 0.25)
    {
        var from = asOf.AddDays(-windowDays);
        var dropped = new Dictionary<string, int>();

        var eligible = Eligible(metric, signature, from, asOf, inputs, dropped);

        // ── An open hold decides first ───────────────────────────────────────────
        //
        // While a step is held, the detector is not asked again: one open hold per metric.
        // A second step during a hold is the finding's business, not a second hold
        // stacked on the first.
        var open = (inputs.Holds ?? []).FirstOrDefault(h =>
            h.IsOpen && h.Metric == metric && h.BaselineSignature == signature);

        if (open is not null)
        {
            var release = Release(open, asOf, eligible, inputs, revertDays, revertFraction);

            if (release is null)
                return Held(metric, signature, asOf, windowDays, minN, open, eligible, inputs, dropped, null);

            if (release.ReleaseReason == "reverted")
            {
                // Back where it was on its own: the old baseline simply continues. No
                // regime start, because nothing was adopted.
                return new BaselineResult(
                    Build(metric, signature, asOf, windowDays, minN, eligible, dropped, null),
                    new HoldChange(HoldChange.Released, release));
            }

            // Explained or accepted: adopt from the change point, as an explained step
            // would have been on the day it was seen.
            var adopted = Drop(eligible, o => o.ObservedDateLocal < open.ChangePointLocal, dropped, "before_regime_change");
            return new BaselineResult(
                Build(metric, signature, asOf, windowDays, minN, adopted, dropped, open.ChangePointLocal),
                new HoldChange(HoldChange.Released, release));
        }

        // ── No hold: detect ──────────────────────────────────────────────────────
        //
        // A sustained step to a new level resets the window rather than contaminating
        // it. Detected on what survives the exclusions above, so a fortnight of illness
        // is not mistaken for a new normal.
        //
        // But only when the step is explained or harmless -- see RegimeDecision. An
        // unexplained step the wrong way is HELD: the baseline is computed from before
        // the change point, so deviation keeps measuring against the person this metric
        // used to describe rather than quietly agreeing that worse is now normal.
        var series = eligible.Select(o => (o.ObservedDateLocal, o.Value)).ToList();
        if (RegimeDetector.Detect(series, regimeShiftZ, regimeDwellDays) is not { } change)
            return new BaselineResult(Build(metric, signature, asOf, windowDays, minN, eligible, dropped, null), null);

        // The same step may already have been held and released. Released by an
        // explanation or by the person: adopt it. Released because it reverted: the
        // remnant the detector still sees is not a new step to hold again.
        //
        // Matched within the dwell, not on the exact day: the split point the detector
        // picks wobbles by a day or two as readings arrive, and an exact match turned one
        // reverted episode into a fresh hold the next morning.
        var previous = (inputs.Holds ?? [])
            .Where(h => h.Metric == metric && h.BaselineSignature == signature && h.Direction == change.Direction
                     && Math.Abs(h.ChangePointLocal.DayNumber - change.ChangePointLocal.DayNumber) <= regimeDwellDays)
            .OrderByDescending(h => h.OpenedLocal)
            .FirstOrDefault();

        var explanation = RegimeDecision.Explain(
            change.ChangePointLocal, inputs.Interventions, inputs.Excluded, inputs.Travel, inputs.Devices);
        var accepted = Accepted(inputs, metric, signature, change.ChangePointLocal);

        if (previous?.ReleaseReason == "reverted")
            return new BaselineResult(Build(metric, signature, asOf, windowDays, minN, eligible, dropped, null), null);

        if (accepted is not null || previous is { ReleaseReason: "explained" or "accepted" }
            || RegimeDecision.ShouldAdopt(metric, change.Direction, explanation))
        {
            var adopted = Drop(eligible, o => o.ObservedDateLocal < change.ChangePointLocal, dropped, "before_regime_change");
            return new BaselineResult(
                Build(metric, signature, asOf, windowDays, minN, adopted, dropped, change.ChangePointLocal), null);
        }

        var hold = new RegimeHold
        {
            Metric = metric,
            BaselineSignature = signature,
            ChangePointLocal = change.ChangePointLocal,
            Direction = change.Direction,
            PreLevelMedian = Math.Round(change.Before, 3),
            PostLevelMedian = Math.Round(change.After, 3),
            OpenedLocal = asOf,
        };

        return Held(metric, signature, asOf, windowDays, minN, hold, eligible, inputs, dropped,
            new HoldChange(HoldChange.Opened, hold));
    }

    // The baseline while a step is held: the window that ended at the change point.
    //
    // This is the part the first version left out. It recorded that the step was not
    // adopted and then kept the readings after the step in the 60-day window, so the
    // mean moved a quarter of the way at detection and all the way within two months.
    private static BaselineResult Held(
        string metric, string signature, DateOnly asOf, int windowDays, int minN,
        RegimeHold hold, List<Observation> current, BaselineInputs inputs,
        Dictionary<string, int> dropped, HoldChange? change)
    {
        // Exclusions are applied afresh to the pre-change window, so a fortnight of
        // illness before the step does not become part of the level being held. The
        // tally is this window's, not the current one's: it is the window being used.
        var held = new Dictionary<string, int>();
        var pre = Eligible(metric, signature, hold.ChangePointLocal.AddDays(-windowDays - 1),
            hold.ChangePointLocal.AddDays(-1), inputs, held);

        var after = current.Count(o => o.ObservedDateLocal >= hold.ChangePointLocal);
        if (after > 0) held["held_regime_shift"] = after;

        // The spread "came back on its own" is judged against, recorded when the hold opens.
        if (change?.Kind == HoldChange.Opened && pre.Count > 0)
            hold.PreLevelSpread = Math.Round(Statistics.MedianAbsoluteDeviation(pre.Select(o => o.Value).ToList()), 3);

        return new BaselineResult(Build(metric, signature, asOf, windowDays, minN, pre, held, null), change);
    }

    // Whether an open hold ends today, and why. Null = it stays.
    private static RegimeHold? Release(
        RegimeHold hold, DateOnly asOf, List<Observation> current, BaselineInputs inputs,
        int revertDays, double revertFraction)
    {
        RegimeHold Closed(string reason, string? explanation) => new()
        {
            Id = hold.Id,
            Metric = hold.Metric,
            BaselineSignature = hold.BaselineSignature,
            ChangePointLocal = hold.ChangePointLocal,
            Direction = hold.Direction,
            PreLevelMedian = hold.PreLevelMedian,
            PreLevelSpread = hold.PreLevelSpread,
            PostLevelMedian = hold.PostLevelMedian,
            OpenedLocal = hold.OpenedLocal,
            ReleasedLocal = asOf,
            ReleaseReason = reason,
            Explanation = explanation,
            CreatedAt = hold.CreatedAt,
        };

        // The person's word first: it is the only input here that is a statement rather
        // than an inference.
        if (Accepted(inputs, hold.Metric, hold.BaselineSignature, hold.ChangePointLocal) is { } accepted)
            return Closed("accepted", accepted.Note);

        // Something added after the fact -- the medication entered a week late, the
        // illness marked once it was over -- now accounts for the step.
        var explanation = RegimeDecision.Explain(
            hold.ChangePointLocal, inputs.Interventions, inputs.Excluded, inputs.Travel, inputs.Devices);
        if (explanation is not null) return Closed("explained", explanation);

        // Back on its own. Without this exit, an illness nobody marked would hold the
        // baseline, and keep its finding open, forever.
        //
        // Measured as how much of the step remains, not in standard deviations: a very
        // steady metric has a spread near zero, and "within 1.5 sigma" of that is a
        // target nothing ever hits. A quarter of the step remaining means the level is
        // most of the way home.
        var recent = current
            .Where(o => o.ObservedDateLocal > asOf.AddDays(-revertDays) && o.ObservedDateLocal >= hold.ChangePointLocal)
            .Select(o => o.Value)
            .ToList();

        var step = Math.Abs(hold.PostLevelMedian - hold.PreLevelMedian);
        if (step > 1e-9 && recent.Count >= revertDays / 2)
        {
            var remaining = Math.Abs(Statistics.Median(recent) - hold.PreLevelMedian) / step;
            if (remaining <= revertFraction) return Closed("reverted", null);
        }

        return null;
    }

    private static RegimeAcceptance? Accepted(BaselineInputs inputs, string metric, string signature, DateOnly changePoint) =>
        (inputs.Acceptances ?? []).FirstOrDefault(a =>
            a.Metric == metric && a.BaselineSignature == signature && a.ChangePointLocal == changePoint);

    // Readings in (fromExclusive, toInclusive] that may shape normal at all.
    private static List<Observation> Eligible(
        string metric, string signature, DateOnly fromExclusive, DateOnly toInclusive,
        BaselineInputs inputs, Dictionary<string, int> dropped)
    {
        var candidates = inputs.Observations
            .Where(o => o.Metric == metric
                     && o.BaselineSignature == signature
                     && o.ObservedDateLocal > fromExclusive
                     && o.ObservedDateLocal <= toInclusive)
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
            .Where(d => d.ActiveFromLocal <= toInclusive)
            .Select(d => (DateOnly?)d.ActiveFromLocal)
            .DefaultIfEmpty(null)
            .Max();

        if (latestDeviceStart is { } deviceStart && deviceStart > fromExclusive)
            eligible = Drop(eligible, o => o.ObservedDateLocal < deviceStart, dropped, "pre_device_change");

        return eligible;
    }

    private static Baseline Build(
        string metric, string signature, DateOnly asOf, int windowDays, int minN,
        List<Observation> eligible, Dictionary<string, int> dropped, DateOnly? regimeStart)
    {
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
