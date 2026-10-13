using System.Reflection;
using System.Text.Json;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Health by domain, and never a score (Vitara ADR 0019).
public class DomainTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 1);

    private static IEnumerable<string> EveryMetricKey() =>
        typeof(MetricKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Concat(MetricCatalogue.All.Select(m => m.Key))
            .Distinct();

    [Fact]
    public void EveryMetricBelongsToAtLeastOneDomain()
    {
        var orphans = EveryMetricKey().Where(k => !Domains.Containing(k).Any()).ToList();
        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryDomainMetricIsCatalogued()
    {
        var unknown = Domains.All.SelectMany(d => d.Metrics).Where(k => MetricCatalogue.Find(k) is null).Distinct().ToList();
        Assert.Empty(unknown);
    }

    private static List<DomainSummary> Build(
        List<TrajectoryResult>? trajectories = null,
        Dictionary<string, (DateOnly, double)>? latest = null,
        List<Finding>? findings = null) =>
        DomainView.Build(
            trajectories ?? Trajectory.ForCatalogue([], [], AsOf),
            latest ?? new(),
            new Dictionary<string, string>(),
            findings ?? [],
            [],
            AsOf);

    private static TrajectoryResult T(string metric, string state) =>
        new(metric, MetricCatalogue.Find(metric)!.Label, Tiers.Dense, state, null, null, null, null, 40, 90,
            null, null, 0, null, "test");

    [Fact]
    public void CountsMatchTheTrajectories()
    {
        var trajectories = Trajectory.ForCatalogue([], [], AsOf)
            .Select(t => t.Metric switch
            {
                MetricKeys.RestingHeartRate => T(t.Metric, TrajectoryStates.Worsening),
                MetricKeys.HrvRmssd => T(t.Metric, TrajectoryStates.Improving),
                MetricKeys.TotalSleepMinutes => T(t.Metric, TrajectoryStates.Stable),
                _ => t,
            }).ToList();

        var sleep = Build(trajectories).Single(d => d.Key == "sleep_and_recovery");

        Assert.Equal(1, sleep.Counts[TrajectoryStates.Worsening]);
        Assert.Equal(1, sleep.Counts[TrajectoryStates.Improving]);
        Assert.Equal(1, sleep.Counts[TrajectoryStates.Stable]);
        Assert.Equal(sleep.Metrics.Count, sleep.Counts.Values.Sum());
        Assert.Contains("1 worsening (resting heart rate)", sleep.Status);
    }

    [Fact]
    public void MetabolicWithGlucoseButNoInsulinNamesTheMissingTest()
    {
        var latest = new Dictionary<string, (DateOnly, double)> { [MetricKeys.Glucose] = (AsOf, 94) };
        var metabolic = Build(latest: latest).Single(d => d.Key == "metabolic");

        Assert.Contains(metabolic.Gaps.MissingKeyTests, g => g.Contains("fasting insulin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(metabolic.Gaps.MissingKeyTests, g => g.Contains("HbA1c"));
    }

    [Fact]
    public void FitnessWithoutAnyStrengthMeasureSaysSo()
    {
        var fitness = Build().Single(d => d.Key == "fitness");
        Assert.Contains(fitness.Gaps.MissingKeyTests, g => g.Contains("Grip strength"));
    }

    [Fact]
    public void AFindingIsListedOncePerDomain()
    {
        var finding = new Finding { Key = "regime_change:resting_hr:up", Metric = MetricKeys.RestingHeartRate, Summary = "x" };
        var domains = Build(findings: [finding, finding]);

        Assert.Single(domains.Single(d => d.Key == "cardiovascular").Findings);
        Assert.Single(domains.Single(d => d.Key == "sleep_and_recovery").Findings);
    }

    [Fact]
    public void ThePayloadHasNoScore()
    {
        // The rule this view exists to keep. Any numeric summary of a domain -- a score,
        // a percentage, a letter, a colour -- is the black box the product argues against.
        var forbidden = new[] { "score", "percent", "percentage", "rating", "grade", "colour", "color", "index" };

        foreach (var d in Build())
        {
            var json = JsonSerializer.SerializeToElement(new
            {
                d.Key, d.Label, d.Question, d.Status, d.Counts, d.Gaps, d.ReadWith, d.Patterns,
            });
            foreach (var prop in json.EnumerateObject())
                Assert.DoesNotContain(prop.Name.ToLowerInvariant(), forbidden);

            Assert.DoesNotContain(typeof(DomainSummary).GetProperties(), p =>
                forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void AgeingWellReadsTheOtherDomainsWithoutRecountingThem()
    {
        var aging = Domains.Find("healthy_aging")!;
        Assert.NotNull(aging.Refers);
        Assert.DoesNotContain(aging.Metrics, m => m == MetricKeys.Vo2Max || m == MetricKeys.WeightKg);
    }
}
