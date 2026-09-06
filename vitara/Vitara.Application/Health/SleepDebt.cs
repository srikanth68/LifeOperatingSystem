namespace Vitara.Application.Health;

public record SleepNeedEstimate(double Minutes, string Basis, bool IsUserSupplied);

// Accumulated sleep deficit, and the estimate it is measured against.
//
// THE SPEC HAS A CIRCULARITY HERE and it is worth being explicit about, because the
// bug is invisible in the output. It says to derive habitual need "from their long-run
// distribution, not a fixed 8 hours" -- which sounds right and is not. Sleep need
// cannot be recovered from sleep duration: if someone is chronically short, taking the
// centre of what they actually sleep computes their deficit as their requirement, and
// then reports, forever, that they are not in debt. The system would be most confident
// exactly when it was most wrong.
//
// There is no way to measure sleep need from duration alone. What is available:
//
//   1. Let the user say. If they know, that beats any inference.
//   2. Failing that, use the UPPER quartile of what they sleep, not the middle. The
//      nights they slept longest are the nights closest to unconstrained -- weekends,
//      holidays, no alarm -- so p75 is a floor on need rather than a description of
//      their habit. Still an underestimate for a chronic under-sleeper, and labelled
//      as an estimate so nobody mistakes it for a measurement.
public static class SleepDebt
{
    // A person's need does not move week to week, so this looks at a long window.
    private const int NeedWindowDays = 90;
    private const int MinNightsForEstimate = 30;

    // Sanity rails on the inferred estimate, and the floor is doing the real work.
    //
    // Seven hours because that is the low end of the adult range, and because a floor
    // at six would not bite: someone sleeping six hours every night has a p75 of six
    // hours, which clamps to six and reports zero debt forever. The floor exists
    // precisely so the estimate cannot collapse onto the habit it is meant to judge.
    //
    // The cost is a false positive for the rare person who genuinely needs less than
    // seven, and they can say so -- VITARA_SLEEP_NEED_MINUTES overrides this entirely.
    // Erring towards "you may be short" is the better mistake here.
    private const double FloorMinutes = 7 * 60;
    private const double CeilingMinutes = 9.5 * 60;

    public static SleepNeedEstimate EstimateNeed(IReadOnlyList<double> nightlyMinutes)
    {
        // An explicit value always wins. This is the only input here that is a fact
        // rather than an inference.
        if (double.TryParse(Environment.GetEnvironmentVariable("VITARA_SLEEP_NEED_MINUTES"), out var stated) && stated > 0)
            return new SleepNeedEstimate(stated, "set by the user", true);

        if (nightlyMinutes.Count < MinNightsForEstimate)
            return new SleepNeedEstimate(8 * 60, $"default, fewer than {MinNightsForEstimate} nights recorded", false);

        // p75, not the mean or median: the longest nights are the least constrained.
        var estimate = Statistics.Percentile(nightlyMinutes, 0.75);
        var clamped = Math.Clamp(estimate, FloorMinutes, CeilingMinutes);

        var basis = Math.Abs(clamped - estimate) > 1
            ? $"estimated from the longest quarter of {nightlyMinutes.Count} nights, clamped to a plausible range"
            : $"estimated from the longest quarter of {nightlyMinutes.Count} nights";

        return new SleepNeedEstimate(clamped, basis, false);
    }

    // Running deficit against that need.
    //
    // Surplus nights pay it down, but only partly: an extra hour on Saturday does not
    // undo an hour lost on each of five weeknights, and a debt that clears completely
    // after one long night would be describing something other than sleep.
    public static double Accumulate(
        IReadOnlyList<(DateOnly Day, double Minutes)> nights, double needMinutes, double surplusRecoveryRate = 0.5)
    {
        var debt = 0.0;

        foreach (var night in nights.OrderBy(n => n.Day))
        {
            var delta = needMinutes - night.Minutes;
            if (delta > 0) debt += delta;
            else debt += delta * surplusRecoveryRate;   // delta is negative here

            // Debt does not go below zero. Banking sleep is not a thing, and a negative
            // running total would silently offset a future genuine deficit.
            debt = Math.Max(0, debt);
        }

        return debt;
    }
}
