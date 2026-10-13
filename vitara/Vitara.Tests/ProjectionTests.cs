using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// A range or a refusal (Vitara ADR 0021).
public class ProjectionTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 1);

    private static List<Trajectory.Point> Daily(double level, double sd, double slopePerDay, int days = 90, int seed = 11)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, days).Select(i =>
        {
            var noise = Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
            return new Trajectory.Point(AsOf.AddDays(-(days - 1) + i), level + slopePerDay * i + sd * noise);
        }).ToList();
    }

    private static ProjectionResult Project(string metric, List<Trajectory.Point> points, int horizon) =>
        Projection.Compute(metric, points, Trajectory.Compute(metric, points, AsOf), horizon);

    [Fact]
    public void ACleanLineIsBracketed_AndTheTrueSlopeIsInsideTheInterval()
    {
        // Resting HR rising 0.05 bpm a day from 52.
        var points = Daily(52, 0.6, 0.05);
        var trajectory = Trajectory.Compute(MetricKeys.RestingHeartRate, points, AsOf);
        var series = points.Select(p => (p.Day.DayNumber, p.Value)).ToList();

        var interval = Statistics.TheilSenInterval(series, trajectory.Variance)!.Value;
        Assert.InRange(0.05, interval.Low, interval.High);

        var p = Projection.Compute(MetricKeys.RestingHeartRate, points, trajectory, 30);
        Assert.True(p.Projected);

        // Truth at +30 days: 52 + 0.05 * (89 + 30).
        var truth = 52 + 0.05 * 119;
        Assert.InRange(truth, p.ProjectedLow!.Value, p.ProjectedHigh!.Value);
        Assert.Equal("C", p.Grade);
        Assert.Contains("If the last", p.Basis);
    }

    [Fact]
    public void AFlatNoisySeriesIsRefused()
    {
        var p = Project(MetricKeys.RestingHeartRate, Daily(54, 1.5, 0), 60);
        Assert.False(p.Projected);
        Assert.Null(p.ProjectedLow);
        Assert.Matches("stable|uncertain|not settled", p.Refusal!);
    }

    [Fact]
    public void ALabIsRefusedByName()
    {
        var p = Projection.Compute(MetricKeys.Ldl,
            [new(AsOf.AddDays(-200), 100, Guid.NewGuid()), new(AsOf.AddDays(-10), 140, Guid.NewGuid())],
            Trajectory.Compute(MetricKeys.Ldl,
                [new(AsOf.AddDays(-200), 100, Guid.NewGuid()), new(AsOf.AddDays(-10), 140, Guid.NewGuid())], AsOf),
            90);

        Assert.False(p.Projected);
        Assert.Contains("lab value", p.Refusal);
    }

    [Fact]
    public void AHorizonLongerThanHalfTheHistoryIsCapped_AndSaysSo()
    {
        var p = Project(MetricKeys.RestingHeartRate, Daily(52, 0.6, 0.05), 180);

        Assert.True(p.Projected);
        Assert.Equal(44, p.HorizonDays);          // half of the 89-day span
        Assert.Contains("half", p.HorizonNote);
    }
}
