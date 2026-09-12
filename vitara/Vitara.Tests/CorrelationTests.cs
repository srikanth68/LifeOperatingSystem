using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Looking for relationships between what the user does and how they recover.
//
// The arithmetic is the easy half. The half that matters is not manufacturing
// discoveries: sixty tests at p<0.05 produce three confident wrong statements every
// single run, and three a week is how a feature stops being read. Most of what is
// pinned down here is refusal.
public class CorrelationTests
{
    private static readonly DateOnly Today = new(2026, 9, 12);

    private static Observation Obs(string metric, DateOnly day, double value) => new()
    {
        Metric = metric,
        ObservedDateLocal = day,
        Value = value,
    };

    // ── Spearman ────────────────────────────────────────────────────────────────

    [Fact]
    public void PerfectlyRankedTogetherIsOne()
    {
        double[] x = [1, 2, 3, 4, 5];
        double[] y = [10, 20, 30, 40, 50];

        Assert.Equal(1.0, Correlations.Spearman(x, y), 6);
    }

    [Fact]
    public void PerfectlyOpposedIsMinusOne()
    {
        double[] x = [1, 2, 3, 4, 5];
        double[] y = [50, 40, 30, 20, 10];

        Assert.Equal(-1.0, Correlations.Spearman(x, y), 6);
    }

    [Fact]
    public void RanksNotValuesIsThePoint()
    {
        // Monotonic but wildly non-linear. Pearson would report about 0.8 here and
        // Spearman reports 1, because the ORDER is perfect -- which is the claim worth
        // making about health data.
        double[] x = [1, 2, 3, 4, 5];
        double[] y = [1, 2, 4, 8, 5000];

        Assert.Equal(1.0, Correlations.Spearman(x, y), 6);
    }

    [Fact]
    public void OneCatastrophicNightDoesNotDragIt()
    {
        // A rank correlation is chosen precisely so a single wrecked reading cannot
        // invent or destroy a relationship. Nine ordered pairs plus one absurd outlier.
        double[] x = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        double[] y = [1, 2, 3, 4, 5, 6, 7, 8, 9, -9000];

        // Still strongly positive: the outlier moved one rank, not the whole fit.
        Assert.True(Correlations.Spearman(x, y) > 0.4);
    }

    [Fact]
    public void TiesTakeTheAverageRank()
    {
        // Step counts and sleep scores repeat. Ranking ties by position instead of
        // average silently mis-orders them.
        var ranks = Correlations.Rank([10, 20, 20, 30]);

        Assert.Equal(1.0, ranks[0]);
        Assert.Equal(2.5, ranks[1]);
        Assert.Equal(2.5, ranks[2]);
        Assert.Equal(4.0, ranks[3]);
    }

    [Fact]
    public void AMetricThatNeverMovedIsUndefinedNotZero()
    {
        // "No relationship" and "nothing to relate" are different answers, and zero
        // would present the second as the first.
        double[] flat = [5, 5, 5, 5, 5];
        double[] moving = [1, 2, 3, 4, 5];

        Assert.True(double.IsNaN(Correlations.Spearman(flat, moving)));
    }

    // ── p-values ────────────────────────────────────────────────────────────────

    [Fact]
    public void AStrongRelationshipOverManyDaysIsUnlikelyByChance()
        => Assert.True(Correlations.PValue(0.6, 90) < 0.001);

    [Fact]
    public void TheSameCoefficientOnFewDaysIsNot()
    {
        // The reason N travels with rho everywhere it is shown. Identical coefficient,
        // completely different claim.
        Assert.True(Correlations.PValue(0.6, 8) > Correlations.PValue(0.6, 90) * 100);
    }

    [Fact]
    public void NoRelationshipIsNotSignificant()
        => Assert.True(Correlations.PValue(0.01, 90) > 0.5);

    // ── Multiple comparisons: the honest part ───────────────────────────────────

    [Fact]
    public void SixtyNoiseTestsProduceNothing()
    {
        // THE TEST THIS FILE EXISTS FOR. Sixty p-values spread uniformly, which is
        // exactly what pure noise looks like. Three of them land under 0.05 and a naive
        // filter would report all three as findings, every run, forever.
        var noise = Enumerable.Range(1, 60)
            .Select(i => new CorrelationResult("a", "b", 0, 0.5, 90, i / 60.0))
            .ToList();

        Assert.Empty(Correlations.Significant(noise));
    }

    [Fact]
    public void AGenuineSignalStillSurvives()
    {
        // Controlling the false discovery rate has to leave real relationships intact,
        // or it has traded one failure for another.
        var results = Enumerable.Range(1, 59)
            .Select(i => new CorrelationResult("a", "b", 0, 0.5, 90, 0.2 + i / 100.0))
            .ToList();

        results.Add(new CorrelationResult("active_calories", "hrv_rmssd", 1, -0.55, 90, 0.000001));

        var kept = Assert.Single(Correlations.Significant(results));
        Assert.Equal("hrv_rmssd", kept.Outcome);
    }

    [Fact]
    public void ATinyButSignificantRelationshipIsNotReported()
    {
        // Over enough days almost anything reaches significance. A rho of 0.12 explains
        // one percent of the variance and is not worth a sentence.
        var results = new List<CorrelationResult>
        {
            new("steps", "sleep_score", 0, 0.12, 400, 0.0000001),
        };

        Assert.Empty(Correlations.Significant(results));
    }

    [Fact]
    public void AddingPairsRaisesTheBarForAllOfThem()
    {
        // Benjamini-Hochberg applied across the whole run, not per pair. Otherwise
        // testing more relationships quietly makes each one easier to pass.
        var borderline = new CorrelationResult("steps", "hrv_rmssd", 1, 0.45, 90, 0.02);

        var alone = Correlations.Significant([borderline]);
        var crowded = Correlations.Significant(
            [borderline, .. Enumerable.Range(1, 40).Select(i => new CorrelationResult("a", "b", 0, 0.4, 90, 0.3 + i / 200.0))]);

        Assert.Single(alone);
        Assert.Empty(crowded);
    }

    // ── The run ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TooFewPairedDaysIsNotTested()
    {
        // Twenty days of a perfect relationship still says nothing: below thirty the
        // interval on rho is so wide that 0.4 is compatible with zero.
        var obs = new List<Observation>();
        for (var i = 0; i < 20; i++)
        {
            var day = Today.AddDays(-i);
            obs.Add(Obs(MetricKeys.ActiveCalories, day, i * 10));
            obs.Add(Obs(MetricKeys.HrvRmssd, day, 100 - i));
        }

        Assert.Empty(Correlations.Run(obs, Today));
    }

    [Fact]
    public void AClearLaggedRelationshipIsFound()
    {
        // Training load today against HRV tomorrow, made deliberately strong. Sixty days
        // so it clears both the minimum and the FDR bar.
        var obs = new List<Observation>();
        var rng = new Random(42);

        for (var i = 0; i < 60; i++)
        {
            var day = Today.AddDays(-60 + i);
            var load = 200 + i * 5 + rng.NextDouble() * 20;

            obs.Add(Obs(MetricKeys.ActiveCalories, day, load));
            // Tomorrow's HRV falls as today's load rises.
            obs.Add(Obs(MetricKeys.HrvRmssd, day.AddDays(1), 120 - load / 10 + rng.NextDouble() * 2));
        }

        var found = Correlations.Run(obs, Today);

        Assert.Contains(found, c =>
            c.Driver == MetricKeys.ActiveCalories &&
            c.Outcome == MetricKeys.HrvRmssd &&
            c.LagDays == 1 &&
            c.Rho < 0);
    }

    [Fact]
    public void PureNoiseAcrossTheRealPairListFindsNothing()
    {
        // The end-to-end version of the sixty-noise-tests case: real metrics, real pair
        // list, random values. Anything reported here is a false discovery.
        var rng = new Random(7);
        var obs = new List<Observation>();

        string[] metrics =
        [
            MetricKeys.ActiveCalories, MetricKeys.Steps, MetricKeys.TotalSleepMinutes,
            MetricKeys.StressHighSeconds, MetricKeys.HrvRmssd, MetricKeys.RestingHeartRate,
            MetricKeys.ReadinessScore, MetricKeys.SleepScore, MetricKeys.SkinTempDeviation,
        ];

        for (var i = 0; i < 90; i++)
        {
            var day = Today.AddDays(-90 + i);
            foreach (var m in metrics) obs.Add(Obs(m, day, rng.NextDouble() * 100));
        }

        Assert.Empty(Correlations.Run(obs, Today));
    }
}
