using Vitara.Insight.Health;
using Vitara.Domain.Entities;

namespace Vitara.Tests;

// Trend detection has to answer two separate questions: how steep, and is it real.
// A slope always exists -- fit a line to noise and a line comes back -- so without the
// second question drift would announce a discovery most days.
public class TrendTests
{
    private static List<(int, double)> Rising(int days, double perDay, double noise = 0)
    {
        var rng = new Random(7);
        return Enumerable.Range(0, days)
            .Select(i => (i, 100 + i * perDay + (noise == 0 ? 0 : (rng.NextDouble() - 0.5) * noise)))
            .ToList();
    }

    [Fact]
    public void TheilSenIgnoresASingleBadReading()
    {
        // The reason least squares was replaced. One mistyped weight should not become
        // a trend anybody acts on.
        var clean = Rising(30, 0.1);
        var withBlunder = new List<(int, double)>(clean) { (15, 900.0) };

        var ols = Statistics.SlopePerDay(withBlunder)!.Value;
        var theilSen = Statistics.TheilSenSlope(withBlunder)!.Value;

        Assert.InRange(theilSen, 0.05, 0.15);          // still ~0.1 per day
        Assert.True(Math.Abs(ols - 0.1) > Math.Abs(theilSen - 0.1),
            $"least squares should be pulled further off ({ols:F3}) than Theil-Sen ({theilSen:F3})");
    }

    [Fact]
    public void ARealTrendIsSignificant()
    {
        var trend = Statistics.Trend(Rising(60, 0.2, noise: 1.0));

        Assert.NotNull(trend);
        Assert.True(trend!.IsSignificant);
        Assert.InRange(trend.SlopePerDay, 0.15, 0.25);
    }

    [Fact]
    public void PureNoiseIsNotATrend()
    {
        // THE POINT OF THE GATE. Random data still yields a slope; it must not yield a
        // finding.
        var rng = new Random(11);
        var flat = Enumerable.Range(0, 60).Select(i => (i, 100 + (rng.NextDouble() - 0.5) * 10)).ToList();

        var trend = Statistics.Trend(flat);
        Assert.NotNull(trend);
        Assert.False(trend!.IsSignificant);
    }

    [Fact]
    public void AShortSeriesSaysNothingRatherThanGuessing()
    {
        // Below about ten points the normal approximation behind Mann-Kendall does not
        // hold, and the honest answer is that the series is too short.
        Assert.Null(Statistics.Trend(Rising(6, 1.0)));
    }

    [Fact]
    public void HeavilyTiedDataDoesNotBreakTheTest()
    {
        // Integer scores and rounded weights tie constantly. Without the ties
        // correction the variance is overstated and the test goes quiet.
        var tied = Enumerable.Range(0, 40).Select(i => (i, (double)(50 + i / 10))).ToList();
        var trend = Statistics.Trend(tied);

        Assert.NotNull(trend);
        Assert.True(trend!.IsSignificant);
    }
}

// Every finding here becomes something the user reads. The failure that matters is
// not a missed detection -- it is one that fires on nothing, twice, after which none
// of them get read.
public class FindingDetectorTests
{
    private static readonly DateOnly Today = new(2026, 9, 1);
    private static readonly HealthThresholdSet Thresholds = new(1.5, -1.5, 1.5);

    private static List<(DateOnly, double)> Z(params double[] values) =>
        values.Select((v, i) => (Today.AddDays(-(values.Length - 1 - i)), v)).ToList();

    // ── Deviation ──

    [Fact]
    public void ASustainedBreachIsAFinding()
    {
        var finding = FindingDetectors.Deviation("resting_hr", Z(2.4, 2.6), 2.0, 2);

        Assert.NotNull(finding);
        Assert.Equal("high", finding!.Direction);
        Assert.Equal("deviation:resting_hr:high", finding.Key);
    }

    [Fact]
    public void OneOddDayDoesNotSpeak()
    {
        // The cheapest and most effective noise filter available.
        Assert.Null(FindingDetectors.Deviation("resting_hr", Z(0.2, 2.6), 2.0, 2));
    }

    [Fact]
    public void BouncingAboveAndBelowIsNotADeviation()
    {
        // Unsettled, not deviating. A finding here would describe the noise rather
        // than the person.
        Assert.Null(FindingDetectors.Deviation("hrv_rmssd", Z(2.4, -2.6), 2.0, 2));
    }

    [Fact]
    public void TheKeyIsStableForTheSameCondition()
    {
        // Identity is what lets the ledger deduplicate. If it moved between runs, the
        // same condition would arrive as a fresh notification every morning -- which is
        // exactly how a channel becomes one to ignore.
        var monday = FindingDetectors.Deviation("resting_hr", Z(2.4, 2.6), 2.0, 2)!;
        var tuesday = FindingDetectors.Deviation("resting_hr", Z(2.6, 2.9), 2.0, 2)!;

        Assert.Equal(monday.Key, tuesday.Key);
    }

    [Fact]
    public void DirectionIsPartOfTheIdentity()
    {
        var high = FindingDetectors.Deviation("resting_hr", Z(2.4, 2.6), 2.0, 2)!;
        var low = FindingDetectors.Deviation("resting_hr", Z(-2.4, -2.6), 2.0, 2)!;

        Assert.NotEqual(high.Key, low.Key);
    }

    // ── Early illness ──

    private static DailyVitals Day(int daysAgo, double? rhr, double? hrv, double? temp) =>
        new(Today.AddDays(-daysAgo), rhr, hrv, temp);

    [Fact]
    public void TwoOfThreeSustainedTriggers()
    {
        var finding = FindingDetectors.EarlyIllness(
            [Day(1, 1.8, -1.7, 0.2), Day(0, 1.9, -1.6, 0.4)], Thresholds, 2);

        Assert.NotNull(finding);
        Assert.Equal("notable", finding!.Severity);
    }

    [Fact]
    public void AllThreeRaisesConfidenceNotCertainty()
    {
        var finding = FindingDetectors.EarlyIllness(
            [Day(1, 1.8, -1.7, 1.6), Day(0, 1.9, -1.9, 1.8)], Thresholds, 2);

        Assert.Equal("high", finding!.Severity);
        Assert.True(finding.Confidence < 1.0, "a signal is never a diagnosis");
    }

    [Fact]
    public void OneMetricMovingIsNotIllness()
    {
        // Resting heart rate alone moves for a late meal, a hard session, a warm room.
        Assert.Null(FindingDetectors.EarlyIllness(
            [Day(1, 2.5, 0.1, 0.0), Day(0, 2.6, 0.2, 0.1)], Thresholds, 2));
    }

    [Fact]
    public void DifferentMetricsOnDifferentDaysIsNotASignal()
    {
        // Resting HR up yesterday and HRV down today is two unrelated days, not two
        // days of a pattern. Each day has to show two of three on its own.
        Assert.Null(FindingDetectors.EarlyIllness(
            [Day(1, 1.8, 0.0, 1.7), Day(0, 0.1, -1.8, 0.0)], Thresholds, 2));
    }

    [Fact]
    public void MissingMetricsDoNotCountAsHits()
    {
        // A night the ring was not worn must not read as evidence of illness.
        Assert.Null(FindingDetectors.EarlyIllness(
            [Day(1, null, null, null), Day(0, null, null, null)], Thresholds, 2));
    }

    // ── Strain ──

    [Theory]
    [InlineData(1.6, "high")]
    [InlineData(0.5, "low")]
    public void AcwrOutOfRangeIsReported(double ratio, string direction)
    {
        var finding = FindingDetectors.Strain(ratio, 0.8, 1.3, Today);
        Assert.Equal(direction, finding!.Direction);
    }

    [Fact]
    public void AcwrInRangeSaysNothing()
        => Assert.Null(FindingDetectors.Strain(1.05, 0.8, 1.3, Today));

    [Fact]
    public void AcwrIsNeverRaisedAboveInfo()
    {
        // Its evidence base is weaker than its popularity: numerator and denominator
        // share data, and the threshold bands have not survived scrutiny well. Worth
        // showing, not worth alarming anyone about.
        Assert.Equal("info", FindingDetectors.Strain(2.4, 0.8, 1.3, Today)!.Severity);
    }

    [Fact]
    public void NoAcwrMeansNoFinding()
        => Assert.Null(FindingDetectors.Strain(null, 0.8, 1.3, Today));

    // ── Drift ──

    [Fact]
    public void AnInsignificantTrendIsNotAFinding()
    {
        var trend = new Statistics.TrendResult(0.4, Z: 1.1, N: 30, IsSignificant: false);
        Assert.Null(FindingDetectors.Drift("weight_kg", trend, 30, Today));
    }

    [Fact]
    public void ASignificantTrendReportsAMonthlyRate()
    {
        // Per day is unreadable for a slow metric. "About 0.6 per month" is the shape
        // of the fact a person can act on.
        var trend = new Statistics.TrendResult(0.02, Z: 3.4, N: 60, IsSignificant: true);
        var finding = FindingDetectors.Drift("weight_kg", trend, 90, Today)!;

        Assert.Equal("rising", finding.Direction);
        Assert.Contains("per month", finding.Summary);
    }

    // ── Staleness ──

    [Fact]
    public void DataThatStoppedArrivingIsAFinding()
    {
        // A ring in a drawer produces the same silence as a week of perfect health.
        var finding = FindingDetectors.Staleness("resting_hr", Today.AddDays(-6), Today, 3);
        Assert.NotNull(finding);
        Assert.Contains("6 days", finding!.Summary);
    }

    [Fact]
    public void ARecentGapIsToleratedSilently()
        => Assert.Null(FindingDetectors.Staleness("resting_hr", Today.AddDays(-2), Today, 3));

    [Fact]
    public void NeverRecordedIsReportedDifferentlyFromStopped()
    {
        var finding = FindingDetectors.Staleness("systolic_bp", null, Today, 3);
        Assert.Contains("ever been recorded", finding!.Summary);

        // "Never logged" and "stopped six days ago" call for different responses, so
        // they must not read as the same finding.
        var stopped = FindingDetectors.Staleness("systolic_bp", Today.AddDays(-6), Today, 3);
        Assert.NotEqual(finding.Summary, stopped!.Summary);
    }

    // ── Regime change ──

    [Fact]
    public void AnUnexplainedStepIsRankedAboveAnExplainedOne()
    {
        // The explained one has already been accounted for by the thing explaining it.
        var change = new RegimeChange(Today.AddDays(-20), 52, 57, 2.1, 20);

        var unexplained = FindingDetectors.FromRegimeChange("resting_hr", change, null, Today);
        var explained = FindingDetectors.FromRegimeChange("resting_hr", change, "started medication X", Today);

        Assert.Equal("notable", unexplained.Severity);
        Assert.Equal("info", explained.Severity);
        Assert.Contains("Nothing recorded explains it", unexplained.Summary);
    }
}
