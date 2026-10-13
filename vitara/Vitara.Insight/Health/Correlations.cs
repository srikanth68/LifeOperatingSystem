using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// One tested relationship between two metrics.
public record CorrelationResult(
    string Driver,
    string Outcome,
    int LagDays,
    double Rho,
    int N,
    double PValue,
    CorrelationSplit? Split = null)
{
    public string Direction => Rho > 0 ? "positive" : "negative";
}

// The same relationship as two numbers a person can compare: the outcome's median on the
// driver's low days and on its high days, cut at the driver's own median. A rho of -0.42
// means nothing to most people; "1 h 32 of deep sleep against 1 h 08" does.
public record CorrelationSplit(double Cut, double LowOutcome, int LowN, double HighOutcome, int HighN);

// Looking for relationships between what the user does and how they recover.
//
// This is the part of the health spec that needs no new data and answers the question
// the whole layer exists for: does eating late wreck my sleep, does a hard session cost
// me HRV tomorrow, does a short night raise my resting heart rate.
//
// THE HARD PART IS NOT THE CORRELATION, IT IS NOT LYING ABOUT IT. Two decisions carry
// that, and without either one this feature manufactures discoveries:
//
//   1. A CURATED PAIR LIST, not every metric against every other. Thirty metrics is 435
//      pairs before lags; at p<0.05 that is twenty-two "findings" a run from pure noise.
//      Pairs are listed where a mechanism is at least plausible, which also means a
//      result has something to be checked against.
//
//   2. FALSE DISCOVERY RATE CONTROL across the whole run. Even sixty tests at p<0.05
//      expect three spurious hits every time. Benjamini-Hochberg raises the bar as the
//      number of tests grows, so adding a pair cannot quietly make the others easier to
//      pass.
//
// Spearman rather than Pearson throughout. Health data is not normal, single bad nights
// are real and common, and a rank correlation is not dragged around by one of them.
public static class Correlations
{
    // Minimum overlapping days before a pair is tested at all.
    //
    // Thirty is not generous. Below it the confidence interval on a correlation is so
    // wide that a reported 0.4 is compatible with 0 -- and a number with that much
    // uncertainty, shown without it, gets acted on as though it were solid.
    public const int MinPairedDays = 30;

    // Reported only if the relationship is strong enough to matter as well as unlikely
    // enough to be real. A statistically significant 0.15 over ninety days is a true
    // relationship that explains two percent of the variance, which is not worth a
    // sentence.
    public const double MinAbsRho = 0.3;

    // Things the user does, and things that respond. Lagged one way only: today's
    // training can affect tomorrow's HRV, and tomorrow's HRV cannot affect today's
    // training. Reversing that is how a correlation engine starts implying nonsense.
    public static readonly string[] Drivers =
    [
        MetricKeys.ActiveCalories,
        MetricKeys.Steps,
        MetricKeys.TotalSleepMinutes,
        MetricKeys.StressHighSeconds,

        // Logged by hand. A driver with fewer than thirty paired days is skipped before
        // it reaches the false-discovery correction, so an unlogged habit cannot raise
        // the bar for the pairs that work.
        MetricKeys.AlcoholUnits,
        MetricCatalogue.CaffeineHoursBeforeBed,
        MetricCatalogue.LastMealHoursBeforeBed,
    ];

    public static readonly string[] Outcomes =
    [
        MetricKeys.HrvRmssd,
        MetricKeys.RestingHeartRate,
        MetricKeys.ReadinessScore,
        MetricKeys.SleepScore,
        MetricKeys.TotalSleepMinutes,
        MetricKeys.SkinTempDeviation,
    ];

    // Same day, and the day after. Nothing longer: the further out the lag, the more
    // tests and the weaker the mechanism, and a seven-day lag on daily data is mostly a
    // test of whether the user had a busy week.
    public static readonly int[] Lags = [0, 1];

    public static List<CorrelationResult> Run(
        IReadOnlyList<Observation> observations, DateOnly asOf, int windowDays = 90)
    {
        var from = asOf.AddDays(-windowDays);

        // One value per metric per day. Several readings in a day are averaged, which
        // is right for the rates here -- a day with two HRV readings is not a day with
        // twice the HRV.
        var series = observations
            .Where(o => o.ObservedDateLocal > from && o.ObservedDateLocal <= asOf)
            .GroupBy(o => o.Metric)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(o => o.ObservedDateLocal)
                      .ToDictionary(d => d.Key, d => d.Average(o => o.Value)));

        var tested = new List<CorrelationResult>();

        foreach (var driver in Drivers)
        {
            if (!series.TryGetValue(driver, out var dSeries)) continue;

            foreach (var outcome in Outcomes)
            {
                if (driver == outcome) continue;
                if (!series.TryGetValue(outcome, out var oSeries)) continue;

                foreach (var lag in Lags)
                {
                    // A metric against itself one day later is autocorrelation, which is
                    // true of nearly every physiological series and tells the user
                    // nothing they can act on.
                    if (lag == 0 && driver == outcome) continue;

                    var (x, y) = Align(dSeries, oSeries, lag);
                    if (x.Count < MinPairedDays) continue;

                    var rho = Spearman(x, y);
                    if (double.IsNaN(rho)) continue;

                    tested.Add(new CorrelationResult(driver, outcome, lag, rho, x.Count, PValue(rho, x.Count), Split(x, y)));
                }
            }
        }

        return Significant(tested);
    }

    // Low days are at or below the driver's median, high days above it. Null when either
    // side has fewer than ten days: a median of four nights is an anecdote. A driver that
    // is mostly one value (no caffeine on most days) puts all of those on the low side,
    // which is the comparison wanted: the usual days against the others.
    public static CorrelationSplit? Split(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count == 0) return null;

        var cut = Statistics.Median(x);
        var low = new List<double>();
        var high = new List<double>();
        for (var i = 0; i < x.Count; i++) (x[i] <= cut ? low : high).Add(y[i]);

        const int minSide = 10;
        if (low.Count < minSide || high.Count < minSide) return null;

        return new CorrelationSplit(cut, Statistics.Median(low), low.Count, Statistics.Median(high), high.Count);
    }

    // Pairs a driver day with the outcome `lag` days later.
    private static (List<double> X, List<double> Y) Align(
        Dictionary<DateOnly, double> driver, Dictionary<DateOnly, double> outcome, int lag)
    {
        var x = new List<double>();
        var y = new List<double>();

        foreach (var (day, value) in driver.OrderBy(d => d.Key))
            if (outcome.TryGetValue(day.AddDays(lag), out var response))
            {
                x.Add(value);
                y.Add(response);
            }

        return (x, y);
    }

    // ── Statistics ──────────────────────────────────────────────────────────────

    // Pearson on ranks. Ties take the average rank, which is what keeps a metric with
    // repeated values -- step counts, sleep scores -- from being silently mis-ranked.
    public static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count || x.Count < 3) return double.NaN;

        var rx = Rank(x);
        var ry = Rank(y);

        var mx = rx.Average();
        var my = ry.Average();

        double num = 0, dx = 0, dy = 0;
        for (var i = 0; i < rx.Count; i++)
        {
            var a = rx[i] - mx;
            var b = ry[i] - my;
            num += a * b;
            dx += a * a;
            dy += b * b;
        }

        // Zero variance in either series -- a metric that never moved over the window.
        // Undefined rather than zero: "no relationship" and "nothing to relate" are
        // different answers.
        if (dx <= 0 || dy <= 0) return double.NaN;

        return num / Math.Sqrt(dx * dy);
    }

    internal static List<double> Rank(IReadOnlyList<double> values)
    {
        var order = values
            .Select((v, i) => (Value: v, Index: i))
            .OrderBy(p => p.Value)
            .ToList();

        var ranks = new double[values.Count];
        var i2 = 0;

        while (i2 < order.Count)
        {
            var j = i2;
            while (j + 1 < order.Count && order[j + 1].Value == order[i2].Value) j++;

            // Average rank across the tie group, 1-based.
            var average = (i2 + j) / 2.0 + 1;
            for (var k = i2; k <= j; k++) ranks[order[k].Index] = average;

            i2 = j + 1;
        }

        return ranks.ToList();
    }

    // Two-sided p via Fisher's z transform.
    //
    // Exact only asymptotically, which is fine because nothing below thirty paired days
    // is tested. An exact t-distribution would need a CDF this project has no library
    // for, and would move the answer in the fourth decimal.
    public static double PValue(double rho, int n)
    {
        if (n < 4) return 1;

        var clamped = Math.Clamp(rho, -0.999999, 0.999999);
        var z = Math.Atanh(clamped) * Math.Sqrt(n - 3.0);

        // Abramowitz & Stegun 7.1.26 in Statistics, accurate to about 1.5e-7 — far
        // tighter than the uncertainty in the correlation itself.
        return 2 * (1 - Statistics.NormalCdf(Math.Abs(z)));
    }

    // Benjamini-Hochberg, then the effect-size floor.
    //
    // The step that makes this honest. Sixty tests at p<0.05 expect three false
    // positives every single run, and three confident wrong statements a week is how a
    // feature stops being read. BH controls the expected PROPORTION of false discoveries
    // instead of the chance of any -- which is the right trade here, where missing a
    // real relationship costs far less than asserting one that is not there.
    //
    // Applied across the whole run, so adding a pair raises the bar for every other
    // pair rather than quietly making them all easier to pass.
    public static List<CorrelationResult> Significant(List<CorrelationResult> tested, double fdr = 0.1)
    {
        if (tested.Count == 0) return [];

        var ordered = tested.OrderBy(t => t.PValue).ToList();
        var m = ordered.Count;
        var cutoff = -1;

        for (var i = 0; i < m; i++)
            if (ordered[i].PValue <= (i + 1) / (double)m * fdr) cutoff = i;

        if (cutoff < 0) return [];

        return ordered
            .Take(cutoff + 1)
            .Where(r => Math.Abs(r.Rho) >= MinAbsRho)
            .ToList();
    }
}
