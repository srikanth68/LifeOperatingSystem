namespace Vitara.Application.Health;

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
    public static double? SlopePerDay(IReadOnlyList<(int Day, double Value)> points)
    {
        if (points.Count < 3) return null;

        var meanX = points.Average(p => (double)p.Day);
        var meanY = points.Average(p => p.Value);

        var numerator = points.Sum(p => (p.Day - meanX) * (p.Value - meanY));
        var denominator = points.Sum(p => (p.Day - meanX) * (p.Day - meanX));

        return denominator <= 1e-9 ? null : numerator / denominator;
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
