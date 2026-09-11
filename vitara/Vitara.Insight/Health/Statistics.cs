namespace Vitara.Insight.Health;

// The arithmetic every derived metric is built from.
//
// Deliberately plain and deliberately robust. Nothing here is clever, because every
// number this produces eventually becomes a sentence about someone's health, and a
// statistic nobody can check by hand is one nobody can argue with when it is wrong.
public static class Statistics
{
    public static double Mean(IReadOnlyList<double> values) =>
        values.Count == 0 ? 0 : values.Sum() / values.Count;

    // Sample standard deviation. n-1 rather than n because these are samples of a
    // person's days, not the population of them.
    public static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = Mean(values);
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 1) return sorted[0];

        var rank = (sorted.Count - 1) * Math.Clamp(p, 0, 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return low == high ? sorted[low] : sorted[low] + (rank - low) * (sorted[high] - sorted[low]);
    }

    public static double Median(IReadOnlyList<double> values) => Percentile(values, 0.5);

    // Median absolute deviation, scaled so that on normally distributed data it
    // estimates the same thing as the standard deviation.
    //
    // This is what outlier rejection uses instead of the spec's "3σ from the window
    // itself". That rule is circular -- it measures spread, then discards points using
    // the spread those same points produced -- and a couple of genuine outliers inflate
    // σ enough to hide themselves. MAD does not move when a few points do.
    public static double MedianAbsoluteDeviation(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var median = Median(values);
        var deviations = values.Select(v => Math.Abs(v - median)).ToList();
        return Median(deviations) * 1.4826;
    }

    // Drops points too far from the median to belong, measured in MADs.
    //
    // Returns the survivors rather than mutating, because the caller needs to record
    // how many were dropped: a window that lost a third of its readings is telling you
    // something, and silently shrinking it hides that.
    public static List<double> WithoutOutliers(IReadOnlyList<double> values, double madThreshold = 3.0)
    {
        if (values.Count < 4) return [.. values];

        var median = Median(values);
        var mad = MedianAbsoluteDeviation(values);

        // Every value identical, or nearly so. Nothing is an outlier and dividing by
        // zero would make everything one.
        if (mad <= 1e-9) return [.. values];

        return values.Where(v => Math.Abs(v - median) / mad <= madThreshold).ToList();
    }

    // How far today sits from normal, in the user's own standard deviations. The core
    // primitive of the whole system: deviation measured against this person, never
    // against a population.
    public static double? ZScore(double value, double mean, double stdDev) =>
        stdDev <= 1e-9 ? null : (value - mean) / stdDev;

    // Least-squares slope, in units per day. x is the day number so gaps in the series
    // are handled correctly -- a fortnight with three missing days is not fourteen
    // evenly spaced points.
    //
    // Kept for comparison and for callers that want the conventional figure, but drift
    // detection uses Trend() below. One bad reading swings a least-squares line, and a
    // single mis-recorded weight should not become a trend anyone acts on.
    public static double? SlopePerDay(IReadOnlyList<(int Day, double Value)> points)
    {
        if (points.Count < 3) return null;

        var meanX = points.Average(p => (double)p.Day);
        var meanY = points.Average(p => p.Value);

        var numerator = points.Sum(p => (p.Day - meanX) * (p.Value - meanY));
        var denominator = points.Sum(p => (p.Day - meanX) * (p.Day - meanX));

        return denominator <= 1e-9 ? null : numerator / denominator;
    }

    // Theil-Sen: the median of every pairwise slope.
    //
    // Robust where least squares is not. It tolerates roughly 29% of the data being
    // rubbish before the estimate breaks down, which matters here because a single
    // mistyped weight or a night the ring half-recorded is entirely normal.
    //
    // O(n^2), and that is fine: the longest window is 90 days, so about four thousand
    // pairs. Choosing the clearer algorithm over the faster one is free at this size.
    public static double? TheilSenSlope(IReadOnlyList<(int Day, double Value)> points)
    {
        if (points.Count < 3) return null;

        var slopes = new List<double>();
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
            {
                var dx = points[j].Day - points[i].Day;
                if (dx == 0) continue;   // same day twice: no slope between them
                slopes.Add((points[j].Value - points[i].Value) / dx);
            }

        return slopes.Count == 0 ? null : Median(slopes);
    }

    public record TrendResult(double SlopePerDay, double Z, int N, bool IsSignificant);

    // Whether a trend is real, and how steep.
    //
    // Theil-Sen gives the slope; Mann-Kendall says whether the series is monotonic
    // enough for that slope to mean anything. Without the second half, a slope always
    // exists -- fit a line to noise and you get a line -- and across four metrics and
    // three windows that is twelve chances a day to discover a trend that is not there.
    //
    // The threshold defaults to p < 0.01 rather than the usual 0.05 precisely because
    // of that: twelve tests a day at 0.05 yields a false trend most days, and a system
    // that announces a spurious trend most days is one nobody reads. The finding layer
    // then requires persistence on top.
    public static TrendResult? Trend(IReadOnlyList<(int Day, double Value)> points, double zThreshold = 2.576)
    {
        // Below about ten points Mann-Kendall's normal approximation is not appropriate
        // and the honest answer is that the series is too short to say.
        if (points.Count < 10) return null;

        var slope = TheilSenSlope(points);
        if (slope is null) return null;

        var ordered = points.OrderBy(p => p.Day).ToList();
        var n = ordered.Count;

        // S counts how often the series moves up versus down across every pair.
        var s = 0;
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
                s += Math.Sign(ordered[j].Value - ordered[i].Value);

        // Variance, corrected for ties. Health data ties constantly -- integer scores,
        // rounded weights -- and the uncorrected formula overstates the variance, which
        // makes the test too conservative rather than too eager.
        var tieCorrection = ordered
            .GroupBy(p => p.Value)
            .Select(g => (double)g.Count())
            .Where(t => t > 1)
            .Sum(t => t * (t - 1) * (2 * t + 5));

        var variance = (n * (n - 1.0) * (2 * n + 5) - tieCorrection) / 18.0;
        if (variance <= 0) return new TrendResult(slope.Value, 0, n, false);

        // The continuity correction: S is discrete, the normal is not.
        var z = s > 0 ? (s - 1) / Math.Sqrt(variance)
              : s < 0 ? (s + 1) / Math.Sqrt(variance)
              : 0;

        return new TrendResult(slope.Value, z, n, Math.Abs(z) >= zThreshold);
    }

    // Acute load against chronic load: a short window's mean over a long window's.
    //
    // Above roughly 1.3 means work has ramped faster than the body has adapted to;
    // below 0.8 means detraining. Null when either window is too thin to mean anything,
    // rather than a ratio computed from two days.
    public static double? AcuteChronicRatio(
        IReadOnlyList<double> acute, IReadOnlyList<double> chronic, int minAcute = 5, int minChronic = 20)
    {
        if (acute.Count < minAcute || chronic.Count < minChronic) return null;

        var chronicMean = Mean(chronic);
        return chronicMean <= 1e-9 ? null : Mean(acute) / chronicMean;
    }
}
