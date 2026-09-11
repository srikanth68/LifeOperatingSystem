using Vitara.Insight.Health;

namespace Vitara.Tests;

// Every number here eventually becomes a sentence about someone's health, so the
// cases that matter are the ones where a plausible-looking method gives a confidently
// wrong answer.
public class HealthStatisticsTests
{
    [Fact]
    public void MedianAbsoluteDeviationIsNotMovedByOutliers()
    {
        // The whole reason outlier rejection uses MAD rather than the standard
        // deviation: a couple of extreme points inflate sigma enough to hide
        // themselves, so a 3-sigma rule stops rejecting the very values it is for.
        var clean = new List<double> { 50, 51, 49, 52, 48, 50, 51 };
        var withOutliers = new List<double>(clean) { 200, 5 };

        var sigmaMoved = Math.Abs(Statistics.StdDev(withOutliers) - Statistics.StdDev(clean));
        var madMoved = Math.Abs(Statistics.MedianAbsoluteDeviation(withOutliers) - Statistics.MedianAbsoluteDeviation(clean));

        Assert.True(madMoved < sigmaMoved / 5,
            $"MAD should barely move ({madMoved:F2}) where sigma lurches ({sigmaMoved:F2})");
    }

    [Fact]
    public void ObviousOutliersAreDropped()
    {
        var values = new List<double> { 50, 51, 49, 52, 48, 50, 51, 400 };
        Assert.DoesNotContain(400, Statistics.WithoutOutliers(values));
    }

    [Fact]
    public void AnIdenticalSeriesLosesNothing()
    {
        // Zero spread means dividing by zero, which would classify every point as
        // infinitely far from the median and throw the whole window away.
        var values = new List<double> { 60, 60, 60, 60, 60, 60 };
        Assert.Equal(6, Statistics.WithoutOutliers(values).Count);
    }

    [Fact]
    public void TinySeriesAreLeftAlone()
        => Assert.Equal(3, Statistics.WithoutOutliers([10, 20, 900]).Count);

    [Fact]
    public void ZScoreIsNullWhenThereIsNoSpreadToMeasureAgainst()
        => Assert.Null(Statistics.ZScore(55, 50, 0));

    [Fact]
    public void SlopeUsesRealDayNumbersNotPositions()
    {
        // A fortnight with missing days is not evenly spaced points. Treating the gaps
        // as absent rather than compressed is the difference between a real trend and
        // an invented one.
        var points = new List<(int, double)> { (0, 100.0), (1, 101.0), (20, 120.0) };
        var slope = Statistics.SlopePerDay(points);

        Assert.NotNull(slope);
        Assert.InRange(slope!.Value, 0.9, 1.1);   // ~1 unit per day, not ~10
    }

    [Fact]
    public void SlopeNeedsThreePoints()
        => Assert.Null(Statistics.SlopePerDay([(0, 1.0), (1, 2.0)]));

    [Fact]
    public void AcwrRefusesToComputeFromTooLittleData()
    {
        // A ratio built from two days is a number, not a signal, and it would be acted
        // on exactly as if it were one.
        var thin = Enumerable.Repeat(100.0, 3).ToList();
        var chronic = Enumerable.Repeat(100.0, 28).ToList();

        Assert.Null(Statistics.AcuteChronicRatio(thin, chronic));
    }

    [Fact]
    public void AcwrSpotsARamp()
    {
        var acute = Enumerable.Repeat(150.0, 7).ToList();
        var chronic = Enumerable.Repeat(100.0, 28).ToList();

        var ratio = Statistics.AcuteChronicRatio(acute, chronic);
        Assert.NotNull(ratio);
        Assert.InRange(ratio!.Value, 1.4, 1.6);
    }
}

// A step change that resets a baseline has to be a real, lasting move. If it is not,
// the detector redefines normal every few days and nothing is ever abnormal again --
// the failure being that everything keeps looking like it works.
public class RegimeDetectorTests
{
    private static List<(DateOnly, double)> Series(params (int Days, double Value)[] runs)
    {
        var points = new List<(DateOnly, double)>();
        var day = new DateOnly(2026, 1, 1);
        var rng = new Random(42);

        foreach (var (days, value) in runs)
            for (var i = 0; i < days; i++)
            {
                points.Add((day, value + (rng.NextDouble() - 0.5)));   // a little noise
                day = day.AddDays(1);
            }

        return points;
    }

    [Fact]
    public void FindsASustainedStepUp()
    {
        // Resting heart rate settles four beats higher and stays there for a month.
        var series = Series((30, 52.0), (30, 56.0));
        var change = RegimeDetector.Detect(series, shiftInSigmas: 1.5, dwellDays: 14);

        Assert.NotNull(change);
        Assert.Equal("up", change!.Direction);
        Assert.InRange(change.After - change.Before, 3.0, 5.0);
    }

    [Fact]
    public void ABriefExcursionIsNotANewNormal()
    {
        // Four days of illness, then back to baseline. Calling that a regime change
        // would reset the baseline TO the illness, and the system would then treat
        // being unwell as normal.
        var series = Series((30, 52.0), (4, 60.0), (20, 52.0));
        Assert.Null(RegimeDetector.Detect(series, shiftInSigmas: 1.5, dwellDays: 14));
    }

    [Fact]
    public void AStepThatHasNotHeldLongEnoughIsNotYetAccepted()
    {
        // Same size of step, only five days old. It may well be real; it has not
        // earned a baseline reset yet.
        var series = Series((30, 52.0), (5, 57.0));
        Assert.Null(RegimeDetector.Detect(series, shiftInSigmas: 1.5, dwellDays: 14));
    }

    [Fact]
    public void OrdinaryNoiseIsNotAStepChange()
    {
        var series = Series((60, 52.0));
        Assert.Null(RegimeDetector.Detect(series, shiftInSigmas: 1.5, dwellDays: 14));
    }

    [Fact]
    public void ATooShortSeriesSaysNothingRatherThanGuessing()
        => Assert.Null(RegimeDetector.Detect(Series((10, 52.0)), 1.5, 14));

    [Fact]
    public void AChangeIsAttributedToSomethingThatOverlapsIt()
    {
        var change = new DateOnly(2026, 6, 10);
        var reason = RegimeDetector.Attribute(change,
            [(new DateOnly(2026, 6, 7), null, "started medication X")]);

        Assert.Equal("started medication X", reason);
    }

    [Fact]
    public void AnUnrelatedInterventionIsNotBlamed()
    {
        // An unexplained regime change is the more interesting finding, so a loose
        // match here would hide the interesting case behind a wrong explanation.
        var reason = RegimeDetector.Attribute(new DateOnly(2026, 6, 10),
            [(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 20), "a January trip")]);

        Assert.Null(reason);
    }
}

// Sleep need cannot be measured from sleep duration. These pin the workaround and,
// more importantly, the failure it exists to prevent.
public class SleepDebtTests
{
    private static List<double> Nights(int count, double minutes) => Enumerable.Repeat(minutes, count).ToList();

    [Fact]
    public void AChronicUnderSleeperIsNotToldTheirDeficitIsTheirRequirement()
    {
        // THE BUG THIS GUARDS. Ninety nights of six hours. Taking the centre of that
        // distribution computes a need of six hours and reports zero debt forever --
        // most confident exactly where it is most wrong.
        var estimate = SleepDebt.EstimateNeed(Nights(90, 6 * 60));

        Assert.True(estimate.Minutes >= 6 * 60, "need must never be set below the floor");
        var debt = SleepDebt.Accumulate(
            Enumerable.Range(0, 7).Select(i => (new DateOnly(2026, 9, 1).AddDays(i), 6.0 * 60)).ToList(),
            estimate.Minutes);

        Assert.True(debt > 0, "a week of six-hour nights must register as debt");
    }

    [Fact]
    public void TheLongestNightsSetTheEstimateNotTheAverage()
    {
        // Weeknights short, weekends long. The unconstrained nights are the ones that
        // say something about need.
        var nights = Nights(60, 6 * 60).Concat(Nights(30, 8.5 * 60)).ToList();
        var estimate = SleepDebt.EstimateNeed(nights);

        Assert.True(estimate.Minutes > 7 * 60, $"expected the upper quartile to lead, got {estimate.Minutes}");
    }

    [Fact]
    public void AnExplicitValueBeatsAnyInference()
    {
        Environment.SetEnvironmentVariable("VITARA_SLEEP_NEED_MINUTES", "465");
        try
        {
            var estimate = SleepDebt.EstimateNeed(Nights(90, 6 * 60));
            Assert.Equal(465, estimate.Minutes);
            Assert.True(estimate.IsUserSupplied);
        }
        finally { Environment.SetEnvironmentVariable("VITARA_SLEEP_NEED_MINUTES", null); }
    }

    [Fact]
    public void TooFewNightsFallsBackAndSaysSo()
    {
        var estimate = SleepDebt.EstimateNeed(Nights(5, 7 * 60));
        Assert.False(estimate.IsUserSupplied);
        Assert.Contains("default", estimate.Basis);
    }

    [Fact]
    public void ShortNightsAccumulateDebt()
    {
        var nights = Enumerable.Range(0, 5)
            .Select(i => (new DateOnly(2026, 9, 1).AddDays(i), 6.0 * 60)).ToList();

        Assert.Equal(5 * 60, SleepDebt.Accumulate(nights, 7 * 60), 1);
    }

    [Fact]
    public void OneLongNightDoesNotWipeOutAWeekOfDeficit()
    {
        // Recovery is partial on purpose. A debt that clears after a single lie-in
        // would be describing something other than sleep.
        var nights = Enumerable.Range(0, 5).Select(i => (new DateOnly(2026, 9, 1).AddDays(i), 6.0 * 60)).ToList();
        nights.Add((new DateOnly(2026, 9, 6), 11.0 * 60));

        Assert.True(SleepDebt.Accumulate(nights, 7 * 60) > 0);
    }

    [Fact]
    public void DebtNeverGoesNegative()
    {
        // A negative running total would silently offset a future real deficit.
        var nights = Enumerable.Range(0, 10).Select(i => (new DateOnly(2026, 9, 1).AddDays(i), 10.0 * 60)).ToList();
        Assert.Equal(0, SleepDebt.Accumulate(nights, 7 * 60));
    }
}
