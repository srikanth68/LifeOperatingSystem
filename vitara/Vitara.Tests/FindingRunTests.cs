using Vitara.Insight.Health;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// The pass that turns computed statistics into things worth saying.
//
// The detectors already had their own tests. What was never covered is the layer that
// decides WHICH of them run against which metrics -- and that layer is where the
// judgement calls live: which metrics may raise a deviation, what gets suppressed when
// the illness signal fires, and what a metric that has never been recorded means.
public class FindingRunTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    private static Baseline Valid(string metric) => new()
    {
        Metric = metric,
        ComputedOnLocal = Today,
        Mean = 55,
        StdDev = 3,
        N = 40,
        IsValid = true,
    };

    private static DerivedMetric Z(string metric, DateOnly day, double z) => new()
    {
        Metric = $"{metric}_z",
        ObservedDateLocal = day,
        Value = z,
    };

    private static FindingRunInputs Inputs(
        IEnumerable<Baseline>? baselines = null,
        IEnumerable<DerivedMetric>? derived = null,
        IEnumerable<Observation>? observations = null) =>
        new(observations?.ToList() ?? [], baselines?.ToList() ?? [], derived?.ToList() ?? [], Today);

    [Fact]
    public void ASustainedDeviationIsReported()
    {
        var findings = FindingRun.Detect(Inputs(
            baselines: [Valid(MetricKeys.TotalSleepMinutes)],
            derived:
            [
                Z(MetricKeys.TotalSleepMinutes, Today.AddDays(-1), -2.4),
                Z(MetricKeys.TotalSleepMinutes, Today, -2.6),
            ]));

        var f = Assert.Single(findings, x => x.Type == FindingTypes.Deviation);
        Assert.Equal(MetricKeys.TotalSleepMinutes, f.Metric);
        Assert.Equal("low", f.Direction);
    }

    [Fact]
    public void ADeviationAgainstAnUnprovenBaselineIsNotReported()
    {
        // A z-score built on four readings is a confident number about nothing, and
        // the confident one is the one that gets acted on.
        var thin = Valid(MetricKeys.TotalSleepMinutes);
        thin.IsValid = false;
        thin.N = 4;

        var findings = FindingRun.Detect(Inputs(
            baselines: [thin],
            derived:
            [
                Z(MetricKeys.TotalSleepMinutes, Today.AddDays(-1), -3.0),
                Z(MetricKeys.TotalSleepMinutes, Today, -3.2),
            ]));

        Assert.DoesNotContain(findings, f => f.Type == FindingTypes.Deviation);
    }

    [Fact]
    public void AMetricOutsideTheCuratedListStaysQuiet()
    {
        // Deep sleep moves with total sleep and REM. Letting every correlated metric
        // raise its own finding turns one poor week into four notifications saying the
        // same thing.
        var findings = FindingRun.Detect(Inputs(
            baselines: [Valid(MetricKeys.DeepSleepMinutes)],
            derived:
            [
                Z(MetricKeys.DeepSleepMinutes, Today.AddDays(-1), -3.0),
                Z(MetricKeys.DeepSleepMinutes, Today, -3.1),
            ]));

        Assert.DoesNotContain(findings, f => f.Type == FindingTypes.Deviation);
    }

    [Fact]
    public void IllnessSuppressesItsOwnComponents()
    {
        // Resting HR up, HRV down, temperature up, sustained. Reporting all three
        // separately alongside the illness finding is four messages about one thing.
        var derived = new List<DerivedMetric>();
        foreach (var day in new[] { Today.AddDays(-1), Today })
        {
            derived.Add(Z(MetricKeys.RestingHeartRate, day, 2.4));
            derived.Add(Z(MetricKeys.HrvRmssd, day, -2.4));
            derived.Add(Z(MetricKeys.SkinTempDeviation, day, 2.4));
        }

        var findings = FindingRun.Detect(Inputs(
            baselines:
            [
                Valid(MetricKeys.RestingHeartRate),
                Valid(MetricKeys.HrvRmssd),
                Valid(MetricKeys.SkinTempDeviation),
            ],
            derived: derived));

        Assert.Contains(findings, f => f.Type == FindingTypes.EarlyIllness);
        Assert.DoesNotContain(findings, f => f.Type == FindingTypes.Deviation);
    }

    [Fact]
    public void AMetricNeverRecordedIsNotStale()
    {
        // Nothing recorded is not the same as stopped recording. Flagging it would be
        // the system complaining about its own emptiness on day one.
        var findings = FindingRun.Detect(Inputs());
        Assert.DoesNotContain(findings, f => f.Type == FindingTypes.Staleness);
    }

    [Fact]
    public void AMetricThatStoppedArrivingIsStale()
    {
        // A ring left in a drawer produces the same silence as a week of perfect
        // health, and only one of those is worth saying nothing about.
        var observations = new List<Observation>
        {
            new()
            {
                Metric = MetricKeys.RestingHeartRate,
                ObservedDateLocal = Today.AddDays(-9),
                Value = 55,
            },
        };

        var findings = FindingRun.Detect(Inputs(observations: observations));

        var stale = Assert.Single(findings, f => f.Type == FindingTypes.Staleness);
        Assert.Equal("missing", stale.Direction);
    }

    [Fact]
    public void KeysAreStableAcrossTwoIdenticalPasses()
    {
        // The property the entire ledger depends on. A key that varies between runs
        // cannot be deduplicated or cooled down, and the same condition arrives as a
        // fresh notification every morning.
        var inputs = Inputs(
            baselines: [Valid(MetricKeys.TotalSleepMinutes)],
            derived:
            [
                Z(MetricKeys.TotalSleepMinutes, Today.AddDays(-1), -2.4),
                Z(MetricKeys.TotalSleepMinutes, Today, -2.6),
            ]);

        var first = FindingRun.Detect(inputs).Select(f => f.Key).ToList();
        var second = FindingRun.Detect(inputs).Select(f => f.Key).ToList();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void NothingWrongProducesNothing()
    {
        var findings = FindingRun.Detect(Inputs(
            baselines: [Valid(MetricKeys.TotalSleepMinutes)],
            derived:
            [
                Z(MetricKeys.TotalSleepMinutes, Today.AddDays(-1), 0.2),
                Z(MetricKeys.TotalSleepMinutes, Today, -0.3),
            ]));

        Assert.Empty(findings);
    }
}
