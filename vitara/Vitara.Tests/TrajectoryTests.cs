using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Five states, never silence (Vitara ADR 0018).
public class TrajectoryTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 1);

    // Ninety daily readings around a level, with seeded noise and an optional slope in
    // units per day.
    private static List<Trajectory.Point> Daily(double level, double sd, double slopePerDay = 0, int days = 90, int seed = 3)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, days).Select(i =>
        {
            var day = AsOf.AddDays(-(days - 1) + i);
            var noise = Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            return new Trajectory.Point(day, level + slopePerDay * i + sd * noise);
        }).ToList();
    }

    [Fact]
    public void AFlatNoisySeriesIsStable()
    {
        var t = Trajectory.Compute(MetricKeys.RestingHeartRate, Daily(54, 1.5), AsOf);
        Assert.Equal(TrajectoryStates.Stable, t.State);
    }

    [Fact]
    public void RestingHeartRateRisingIsWorsening_AndHrvRisingIsImproving()
    {
        // About 4 bpm over 90 days, through the same noise: lower is better here.
        var rhr = Trajectory.Compute(MetricKeys.RestingHeartRate, Daily(52, 1.5, 0.045), AsOf);
        Assert.Equal(TrajectoryStates.Worsening, rhr.State);
        Assert.Equal("up", rhr.Direction);

        // And higher is better for HRV.
        var hrv = Trajectory.Compute(MetricKeys.HrvRmssd, Daily(45, 4, 0.12), AsOf);
        Assert.Equal(TrajectoryStates.Improving, hrv.State);
    }

    [Fact]
    public void ATinyCleanSlopeIsUncertain_NotWorsening()
    {
        // 0.2 bpm over 90 days with almost no noise: certain, and trivial.
        var t = Trajectory.Compute(MetricKeys.RestingHeartRate, Daily(54, 0.02, 0.2 / 90), AsOf);
        Assert.Equal(TrajectoryStates.Uncertain, t.State);
        Assert.Contains("too small", t.Reason);
    }

    [Fact]
    public void TenDailyReadingsAreNotEnough_AndSaySo()
    {
        var t = Trajectory.Compute(MetricKeys.RestingHeartRate, Daily(54, 1.5, days: 10), AsOf);
        Assert.Equal(TrajectoryStates.InsufficientData, t.State);
        Assert.Contains("10 readings", t.Reason);
    }

    [Fact]
    public void ALabDrawnOnceHasNoDirection()
    {
        var t = Trajectory.Compute(MetricKeys.Ldl, [new(AsOf.AddDays(-30), 120, Guid.NewGuid())], AsOf);
        Assert.Equal(TrajectoryStates.InsufficientData, t.State);
        Assert.Contains("second draw", t.Reason);
    }

    [Fact]
    public void TwoDrawsBeyondTheScaleAreWorsening_WithLowConfidence()
    {
        // 100 to 135: more than a quarter of the previous value.
        var t = Trajectory.Compute(MetricKeys.Ldl,
            [new(AsOf.AddDays(-200), 100, Guid.NewGuid()), new(AsOf.AddDays(-10), 135, Guid.NewGuid())], AsOf);

        Assert.Equal(TrajectoryStates.Worsening, t.State);
        Assert.Contains("Low confidence", t.Reason);
        Assert.Null(t.SlopePerMonth);   // never a slope on the sparse tier
    }

    [Fact]
    public void WeightLastLoggedTwoMonthsAgoIsNotATrendAboutToday()
    {
        var points = Enumerable.Range(0, 40)
            .Select(i => new Trajectory.Point(AsOf.AddDays(-62 - i * 3), 80 - i * 0.05))
            .ToList();

        var t = Trajectory.Compute(MetricKeys.WeightKg, points, AsOf);

        Assert.Equal(TrajectoryStates.InsufficientData, t.State);
        Assert.Contains("62 days ago", t.Reason);
    }

    [Fact]
    public void AMetricWithNoBetterDirectionRisesRatherThanImproves()
    {
        var t = Trajectory.Compute(MetricKeys.Steps, Daily(7000, 800, 25), AsOf);
        Assert.Equal(TrajectoryStates.Rising, t.State);
    }

    [Fact]
    public void EveryCatalogueMetricGetsExactlyOneState_EvenWithNoData()
    {
        var rows = Trajectory.ForCatalogue([], [], AsOf);

        Assert.Equal(MetricCatalogue.All.Count, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Contains(r.State, TrajectoryStates.All);
            Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        });
    }

    [Fact]
    public void AnIllnessPeriodIsLeftOutOfTheTrend()
    {
        // Flat, except a week of flu at the end that would otherwise read as a rise.
        var obs = Daily(54, 1.0).Select((p, i) => new Observation
        {
            Metric = MetricKeys.RestingHeartRate,
            ObservedDateLocal = p.Day,
            Value = i >= 80 ? p.Value + 8 : p.Value,
        }).ToList();
        var flu = new List<ExcludedPeriod> { new() { StartLocal = AsOf.AddDays(-9), EndLocal = AsOf, Reason = "illness" } };

        var row = Trajectory.ForCatalogue(obs, [], AsOf, excluded: flu)
            .Single(r => r.Metric == MetricKeys.RestingHeartRate);

        Assert.NotEqual(TrajectoryStates.Worsening, row.State);
    }
}
