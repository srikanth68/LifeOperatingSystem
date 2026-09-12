using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// One row per night, not one per session.
//
// San reported fifty-one hours of sleep debt. Oura's `sleep` endpoint returns every
// sleep period it detects -- naps, "rest" periods and a good deal of noise -- and
// SleepSession has no type field, so each one became its own observation and each was
// counted as a night.
//
// The shapes below come from a real database: several few-minute sessions beside the
// actual night, and zero-minute sessions.
//
// Against a need of about seven hours, each of those junk rows contributed a full
// night's deficit -- a single such day manufactured around twenty-one hours.
public class SleepProjectionTests
{
    private static readonly DateOnly Day = new(2026, 1, 15);

    private static SleepSession Session(
        int minutes, int hour = 7, double? hrv = null, double? temp = null, int? score = null, string? id = null) => new()
    {
        Id = id ?? $"s{minutes}-{hour}",
        Day = Day,
        TotalSleepMinutes = minutes,
        DeepMinutes = minutes / 5,
        RemMinutes = minutes / 4,
        BedtimeEnd = Day.ToDateTime(new TimeOnly(hour, 0)),
        AvgHrv = hrv,
        SkinTempDeviation = temp,
        Score = score,
    };

    private static double Value(IEnumerable<Observation> obs, string metric) =>
        obs.Single(o => o.Metric == metric).Value;

    [Fact]
    public void TheRealDayThatCausedThis()
    {
        // Four sessions became four nights. They are one night.
        var obs = ObservationProjector.FromSleep(
            [Session(13, 3), Session(9, 4), Session(5, 5), Session(382, 7)]);

        Assert.Equal(409, Value(obs, MetricKeys.TotalSleepMinutes));
        Assert.Single(obs.Where(o => o.Metric == MetricKeys.TotalSleepMinutes));
    }

    [Fact]
    public void AZeroMinuteSessionDoesNotBecomeANight()
    {
        // A zero-minute session beside the real night was a full night's deficit on its own.
        var obs = ObservationProjector.FromSleep([Session(0, 2), Session(504, 7)]);

        Assert.Equal(504, Value(obs, MetricKeys.TotalSleepMinutes));
    }

    [Fact]
    public void EveryMetricCollapsesToOneRow()
    {
        // Not just total sleep. The baseline for each of these was pooling nap values
        // with night values, so the mean collapsed and the spread exploded -- and every
        // z-score computed against them was meaningless.
        var obs = ObservationProjector.FromSleep(
            [Session(13, 3, hrv: 20, temp: 0.9, score: 30), Session(382, 7, hrv: 44, temp: 0.1, score: 82)]);

        foreach (var group in obs.GroupBy(o => o.Metric))
            Assert.Single(group);
    }

    [Fact]
    public void PointInTimeReadingsComeFromTheLongestSession()
    {
        // A 13-minute mis-detection's HRV is not the night's HRV, and its skin
        // temperature is the number the illness detector reads. Summing these would be
        // meaningless and averaging them corrupts a real reading with a fake one.
        var obs = ObservationProjector.FromSleep(
            [Session(13, 3, hrv: 20, temp: 0.9, score: 30), Session(382, 7, hrv: 44, temp: 0.1, score: 82)]);

        Assert.Equal(44, Value(obs, MetricKeys.HrvRmssd));
        Assert.Equal(0.1, Value(obs, MetricKeys.SkinTempDeviation), 3);
        Assert.Equal(82, Value(obs, MetricKeys.SleepScore));
    }

    [Fact]
    public void AGenuineNapStillCounts()
    {
        // Summing needs no threshold, which is why it beats trying to decide what is a
        // real nap. Forty minutes on top of a short night is forty minutes of sleep.
        var obs = ObservationProjector.FromSleep([Session(300, 7), Session(40, 15)]);

        Assert.Equal(340, Value(obs, MetricKeys.TotalSleepMinutes));
    }

    [Fact]
    public void DeepAndRemSumToo()
    {
        // Additive like total sleep. Taking them from the main session only would lose
        // a real nap's contribution while total sleep kept it, leaving the three
        // inconsistent with each other.
        var obs = ObservationProjector.FromSleep([Session(300, 7), Session(40, 15)]);

        Assert.Equal(300 / 5 + 40 / 5, Value(obs, MetricKeys.DeepSleepMinutes));
        Assert.Equal(300 / 4 + 40 / 4, Value(obs, MetricKeys.RemSleepMinutes));
    }

    [Fact]
    public void TheInstantComesFromTheMainSession()
    {
        // The unique index keys on the instant. If it moved when Oura added a nap, the
        // old row would survive beside the new one and the day would be double-counted
        // all over again.
        var obs = ObservationProjector.FromSleep([Session(13, 3), Session(382, 7)]);

        Assert.All(obs, o => Assert.Equal(Day.ToDateTime(new TimeOnly(7, 0)), o.ObservedAtLocal));
    }

    [Fact]
    public void ANormalSingleNightIsUnchanged()
    {
        // The overwhelmingly common case has to come through exactly as before.
        var obs = ObservationProjector.FromSleep([Session(430, 7, hrv: 42, score: 79)]);

        Assert.Equal(430, Value(obs, MetricKeys.TotalSleepMinutes));
        Assert.Equal(42, Value(obs, MetricKeys.HrvRmssd));
    }

    [Fact]
    public void NoSessionsProducesNothing()
        => Assert.Empty(ObservationProjector.FromSleep([]));

    [Fact]
    public void AMissingReadingIsStillOmittedNotZeroed()
    {
        // The rule that survived the rewrite: a night with no HRV recorded must not
        // contribute an HRV of zero to the baseline.
        var obs = ObservationProjector.FromSleep([Session(430, 7)]);

        Assert.DoesNotContain(obs, o => o.Metric == MetricKeys.HrvRmssd);
    }
}
