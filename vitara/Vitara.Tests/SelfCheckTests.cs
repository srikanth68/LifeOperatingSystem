using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// The audit of the audit.
//
// This page exists to separate "nothing is happening" from "I cannot see", so every
// test here is about that distinction holding. A self-check that reports a blind
// detector as quiet is worse than no self-check: it is a reassuring answer about a
// system that is not looking.
public class SelfCheckTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static Observation Obs(string metric, double value, DateOnly day, string tier = Tiers.Dense, Guid? panel = null) => new()
    {
        Metric = metric,
        Value = value,
        ObservedDateLocal = day,
        ObservedAtLocal = day.ToDateTime(new TimeOnly(8, 0)),
        Tier = tier,
        LabPanelId = panel,
    };

    private static List<Observation> Daily(string metric, double value, int days, string tier = Tiers.Dense)
    {
        // Seeded off the metric name, so two series are independent. An earlier version
        // gave every metric the same sawtooth; they correlated perfectly and the
        // correlation engine was entirely right to say so.
        // A seed that is the same on every run. string.GetHashCode() is randomised per process in
        // .NET, so seeding from it gave different "random" data each run, and now and then the
        // noise lined up into a correlation and a test about finding nothing found something.
        var rng = new Random(metric.Aggregate(17, (h, c) => unchecked(h * 31 + c)) & 0x7fffffff);
        return Enumerable.Range(0, days)
            .Select(i => Obs(metric, value + (rng.NextDouble() - 0.5) * value * 0.2, Today.AddDays(-days + 1 + i), tier))
            .ToList();
    }

    private static Baseline Valid(string metric, bool isValid = true) => new()
    {
        Metric = metric, ComputedOnLocal = Today, Mean = 55, StdDev = 3,
        N = isValid ? 40 : 6, IsValid = isValid,
    };

    private static SelfCheck.Inputs Inputs(
        IEnumerable<Observation>? observations = null,
        IEnumerable<Baseline>? baselines = null,
        IEnumerable<DerivedMetric>? derived = null,
        IEnumerable<Finding>? findings = null,
        IEnumerable<Intervention>? interventions = null) =>
        new(observations?.ToList() ?? [], derived?.ToList() ?? [], baselines?.ToList() ?? [],
            findings?.ToList() ?? [], interventions?.ToList() ?? [], [], ReferenceRanges.Seed, Today);

    private static Capability Find(SelfCheckResult r, string key) => r.Capabilities.First(c => c.Key == key);

    // ── The distinction the whole thing exists for ──────────────────────────────

    [Fact]
    public void AnEmptySystemIsBlindRatherThanQuiet()
    {
        // The failure this page is built to prevent. With no data at all, every other
        // surface shows an empty findings list — which reads exactly like a clean bill
        // of health.
        var result = SelfCheck.Run(Inputs());

        Assert.Equal(0, result.Speaking);
        Assert.True(result.Blind > 0);
        Assert.All(result.Capabilities.Where(c => c.State == SelfCheck.Blind),
            c => Assert.False(string.IsNullOrWhiteSpace(c.Says)));
    }

    [Fact]
    public void ABlindCapabilityAlwaysSaysWhatWouldUnblockIt()
    {
        // "Cannot run" with no reason is the same silence this page exists to break.
        var result = SelfCheck.Run(Inputs());

        var blindWithoutNeeds = result.Capabilities
            .Where(c => c.State == SelfCheck.Blind && c.Needs.Count == 0)
            .Select(c => c.Key)
            .ToList();

        // Staleness is the one detector that needs nothing, so it is never blind; if
        // anything else is blind without a remedy, the page has lost its point.
        Assert.Empty(blindWithoutNeeds);
    }

    [Fact]
    public void ADetectorWithDataButNothingToReportIsQuietNotBlind()
    {
        // The other half of the distinction. A settled baseline and no deviation means
        // nothing is unusual, which is real information and must not be filed with
        // "could not look".
        var result = SelfCheck.Run(Inputs(
            observations: Daily(MetricKeys.RestingHeartRate, 54, 120),
            baselines: FindingRun.DeviationMetrics.Select(m => Valid(m))));

        Assert.Equal(SelfCheck.Quiet, Find(result, "detector:deviation").State);
    }

    [Fact]
    public void ADetectorThatHasFiredBeforeIsSpeaking()
    {
        var result = SelfCheck.Run(Inputs(
            baselines: [Valid(MetricKeys.RestingHeartRate)],
            findings:
            [
                new Finding { Type = FindingTypes.Deviation, Metric = MetricKeys.RestingHeartRate, Key = "a" },
                new Finding { Type = FindingTypes.Deviation, Metric = MetricKeys.HrvRmssd, Key = "b" },
            ]));

        var deviation = Find(result, "detector:deviation");

        Assert.Equal(SelfCheck.Speaking, deviation.State);
        Assert.Equal(2, deviation.EverSaid);
    }

    [Fact]
    public void ResolvedFindingsStillCountAsHavingSpoken()
    {
        // The question is what this system has ever told its user, not what it is
        // saying this morning. A detector that fired eleven times and resolved each one
        // is working.
        var result = SelfCheck.Run(Inputs(
            baselines: [Valid(MetricKeys.RestingHeartRate)],
            findings: [new Finding
            {
                Type = FindingTypes.Deviation, Metric = MetricKeys.RestingHeartRate, Key = "a",
                ResolvedLocal = Today.AddDays(-30),
            }]));

        Assert.Equal(1, Find(result, "detector:deviation").EverSaid);
    }

    // ── Baselines ───────────────────────────────────────────────────────────────

    [Fact]
    public void ALearningBaselineIsNotAReadyOne()
    {
        var result = SelfCheck.Run(Inputs(
            baselines: [Valid(MetricKeys.RestingHeartRate, isValid: false)]));

        var baselines = Find(result, "baselines");

        Assert.Equal(SelfCheck.Blind, baselines.State);
        Assert.Contains(baselines.Needs, n => n.Contains("still learning"));
    }

    [Fact]
    public void TheBaselineFloorIsStatedRatherThanImplied()
    {
        var says = Find(SelfCheck.Run(Inputs()), "baselines").Says;

        Assert.Contains(HealthThresholds.MinBaselineN.ToString(), says);
    }

    // ── Labs: one draw is a position, two are a direction ───────────────────────

    [Fact]
    public void OneDrawIsQuietAndTwoAreSpeaking()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var one = SelfCheck.Run(Inputs(observations:
            [Obs(MetricKeys.Ldl, 110, Today.AddDays(-200), Tiers.Sparse, first)]));

        var two = SelfCheck.Run(Inputs(observations:
        [
            Obs(MetricKeys.Ldl, 110, Today.AddDays(-200), Tiers.Sparse, first),
            Obs(MetricKeys.Ldl, 150, Today.AddDays(-10), Tiers.Sparse, second),
        ]));

        Assert.Equal(SelfCheck.Quiet, Find(one, "labs").State);
        Assert.Contains("not yet a direction", Find(one, "labs").Says);
        Assert.Equal(SelfCheck.Speaking, Find(two, "labs").State);
    }

    [Fact]
    public void NoBloodWorkIsBlindAndSaysHowManyAnalytesAreWaiting()
    {
        var labs = Find(SelfCheck.Run(Inputs()), "labs");

        Assert.Equal(SelfCheck.Blind, labs.State);
        Assert.Contains("waiting", labs.Says);
    }

    // ── Relationships ───────────────────────────────────────────────────────────

    [Fact]
    public void NoTestablePairIsBlindRatherThanNoRelationships()
    {
        // The most misleading silence in the system. "No relationships found" from an
        // engine that could not test a single pair is not a finding about the body.
        var relationships = Find(SelfCheck.Run(Inputs()), "relationships");

        Assert.Equal(SelfCheck.Blind, relationships.State);
        Assert.Contains(Correlations.MinPairedDays.ToString(), relationships.Says);
    }

    [Fact]
    public void TestablePairsThatFoundNothingAreQuietAndSayThatIsAnAnswer()
    {
        var observations = Daily(MetricKeys.ActiveCalories, 500, 80)
            .Concat(Daily(MetricKeys.HrvRmssd, 60, 80))
            .Concat(Daily(MetricKeys.RestingHeartRate, 54, 80))
            .ToList();

        var relationships = Find(SelfCheck.Run(Inputs(observations: observations)), "relationships");

        Assert.Equal(SelfCheck.Quiet, relationships.State);
        Assert.Contains("real answer", relationships.Says);
    }

    // ── Forecast ────────────────────────────────────────────────────────────────

    [Fact]
    public void AForecastWithNoHistoryIsBlindAndNamesTheTrainingFloor()
    {
        var forecast = SelfCheck.Run(Inputs()).Capabilities
            .First(c => c.Group == "Forecast");

        Assert.Equal(SelfCheck.Blind, forecast.State);
        Assert.Contains(Prediction.MinTrainingDays.ToString(), forecast.Says);
    }

    // ── Interventions ───────────────────────────────────────────────────────────

    [Fact]
    public void AnInterventionWithNoTargetIsCountedAsUnabletoEverGetAVerdict()
    {
        var result = SelfCheck.Run(Inputs(interventions:
        [
            new Intervention { Name = "Magnesium", StartedOnLocal = Today.AddDays(-300), TargetMetric = null },
        ]));

        var row = Find(result, "interventions");

        Assert.Contains(row.Needs, n => n.Contains("never get a verdict"));
    }

    // ── Coverage ────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryCatalogueMetricGetsACoverageRowIncludingTheEmptyOnes()
    {
        // An absent row and a row of zeroes are the same distinction again: a metric
        // missing from this table is indistinguishable from one the system does not
        // support.
        var result = SelfCheck.Run(Inputs());

        Assert.Equal(MetricCatalogue.All.Count, result.Coverage.Count);
        Assert.All(result.Coverage, c => Assert.Equal("never", c.State));
    }

    [Fact]
    public void AMetricThatStoppedArrivingIsStaleRatherThanCurrent()
    {
        var old = Daily(MetricKeys.RestingHeartRate, 54, 60)
            .Select(o => Obs(o.Metric, o.Value, o.ObservedDateLocal.AddDays(-90)))
            .ToList();

        var row = SelfCheck.Run(Inputs(observations: old))
            .Coverage.First(c => c.Metric == MetricKeys.RestingHeartRate);

        Assert.Equal("stale", row.State);
        Assert.True(row.DaysSinceLast > 60);
    }

    // ── The rule this page must not break ───────────────────────────────────────

    [Fact]
    public void ThereIsNoOverallScore()
    {
        // A single number summarising how well the system is working would be exactly
        // the kind of composite it grades as experimental everywhere else. Three counts
        // and a list, and a headline that says so out loud.
        var result = SelfCheck.Run(Inputs());

        Assert.Equal(result.Capabilities.Count, result.Speaking + result.Quiet + result.Blind);
        Assert.Contains(result.Headline, h => h.Contains("No overall figure"));
    }

    [Fact]
    public void TheHeadlineSeparatesCannotSeeFromNothingHappening()
    {
        var result = SelfCheck.Run(Inputs(
            observations: Daily(MetricKeys.RestingHeartRate, 54, 120),
            baselines: FindingRun.DeviationMetrics.Select(m => Valid(m))));

        Assert.Contains(result.Headline, h => h.Contains("not about your health"));
    }

    [Fact]
    public void EveryCapabilityIsReadableWithoutFurtherExplanation()
    {
        var result = SelfCheck.Run(Inputs(observations: Daily(MetricKeys.RestingHeartRate, 54, 120)));

        Assert.All(result.Capabilities, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Label), $"{c.Key} has no label");
            Assert.False(string.IsNullOrWhiteSpace(c.Says), $"{c.Key} says nothing");
            Assert.Contains(c.State, new[] { SelfCheck.Speaking, SelfCheck.Quiet, SelfCheck.Blind });
        });
    }

    [Fact]
    public void TheAuditReadsTheRealDetectorListsRatherThanACopy()
    {
        // If these ever diverge, the self-check reports the health of a configuration
        // that is not the one running.
        var deviation = Find(SelfCheck.Run(Inputs()), "detector:deviation");

        // With no baselines at all, every metric on the real list should appear as
        // something to fix — by its catalogue label, not its key.
        Assert.Equal(FindingRun.DeviationMetrics.Length, deviation.Needs.Count);

        foreach (var metric in FindingRun.DeviationMetrics)
            Assert.Contains(deviation.Needs, n => n.StartsWith(MetricCatalogue.Find(metric)!.Label));
    }
}
