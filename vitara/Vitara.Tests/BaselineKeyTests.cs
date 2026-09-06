using Vitara.Domain.Health;

namespace Vitara.Tests;

// Which context splits a baseline, and which is merely recorded.
//
// The spec says baselines are computed per (metric, context signature) and lists eight
// context fields. Read literally that is dozens of buckets per metric, each needing
// twenty-plus readings before it means anything -- nobody accumulates twenty
// morning-seated-left-arm-fasted-no-caffeine-no-alcohol blood pressures, so every
// bucket stays permanently invalid and the feature silently never works.
//
// So each metric declares only the context that moves the number enough to be worth
// halving the sample for. Everything else is still stored on the reading, where it can
// explain an outlier, without fragmenting the baseline.
public class BaselineKeyTests
{
    [Fact]
    public void OuraMetricsDoNotSplitOnContextAtAll()
    {
        // Measured by a device overnight under conditions the user does not vary.
        // Splitting these would halve the sample for no gain.
        Assert.Empty(BaselineKeys.For(MetricKeys.HrvRmssd));
        Assert.Equal("", BaselineKeys.Signature(MetricKeys.HrvRmssd, new MeasurementContext(TimeOfDay: "night")));
    }

    [Fact]
    public void BloodPressureSplitsOnPostureAndTimeOfDayOnly()
    {
        var fields = BaselineKeys.For(MetricKeys.SystolicBp);

        Assert.Contains("position", fields);
        Assert.Contains("timeOfDay", fields);
        // Recorded on the reading, deliberately not a split: the difference between
        // arms is smaller than the cost of halving an already-sparse sample.
        Assert.DoesNotContain("arm", fields);
        Assert.DoesNotContain("caffeine", fields);
    }

    [Fact]
    public void TheSameConditionsAlwaysProduceTheSameSignature()
    {
        // It is stored on every row and compared across runs, so it has to be stable.
        var a = BaselineKeys.Signature(MetricKeys.SystolicBp, new MeasurementContext(Position: "seated", TimeOfDay: "waking"));
        var b = BaselineKeys.Signature(MetricKeys.SystolicBp, new MeasurementContext(Position: "seated", TimeOfDay: "waking", Arm: "left"));

        Assert.Equal(a, b);   // arm does not participate
        Assert.Contains("position=seated", a);
        Assert.Contains("timeOfDay=waking", a);
    }

    [Fact]
    public void DifferentPostureIsADifferentBaseline()
    {
        var seated = BaselineKeys.Signature(MetricKeys.SystolicBp, new MeasurementContext(Position: "seated", TimeOfDay: "waking"));
        var supine = BaselineKeys.Signature(MetricKeys.SystolicBp, new MeasurementContext(Position: "supine", TimeOfDay: "waking"));

        Assert.NotEqual(seated, supine);
    }

    [Fact]
    public void GlucoseSplitsOnFastingAndNothingElse()
    {
        // Fasting and post-meal glucose are different quantities sharing a name.
        // Splitting further would leave neither half usable.
        Assert.Equal(["fasting"], BaselineKeys.For(MetricKeys.Glucose));

        var fasted = BaselineKeys.Signature(MetricKeys.Glucose, new MeasurementContext(Fasting: true));
        var fed = BaselineKeys.Signature(MetricKeys.Glucose, new MeasurementContext(Fasting: false));
        Assert.NotEqual(fasted, fed);
    }

    [Fact]
    public void AGlucoseReadingWithoutTheFastingFlagCannotBeBaselined()
    {
        // It is stored and displayed; it just belongs to neither bucket, and pooling it
        // would corrupt whichever one it landed in.
        Assert.False(BaselineKeys.CanBaseline(MetricKeys.Glucose, new MeasurementContext()));
        Assert.False(BaselineKeys.CanBaseline(MetricKeys.Glucose, null));
        Assert.True(BaselineKeys.CanBaseline(MetricKeys.Glucose, new MeasurementContext(Fasting: true)));
    }

    [Fact]
    public void AMetricThatNeedsNoContextIsAlwaysBaselineable()
        => Assert.True(BaselineKeys.CanBaseline(MetricKeys.RestingHeartRate, null));

    [Fact]
    public void PartialContextIsNotEnoughForAMetricThatNeedsTwoFields()
        => Assert.False(BaselineKeys.CanBaseline(MetricKeys.SystolicBp, new MeasurementContext(Position: "seated")));
}

// Day boundaries are the user's, not the server's. New York runs four or five hours
// behind UTC, so anything logged after early evening is already tomorrow in UTC --
// which silently files an evening reading, or a night's sleep, under the wrong day.
public class LocalTimeTests
{
    [Fact]
    public void LateEveningLocalIsStillTodayNotTomorrow()
    {
        // 11pm on 1 September in New York is 3am on the 2nd in UTC. Bucketing by UTC
        // puts that night's sleep on the wrong day, and every baseline built on top
        // inherits it.
        var utc = new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 9, 1), LocalTime.DayOf(utc));
    }

    [Fact]
    public void EarlyMorningUtcBelongsToThePreviousLocalDay()
    {
        var utc = new DateTime(2026, 9, 1, 2, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateOnly(2026, 8, 31), LocalTime.DayOf(utc));
    }

    [Fact]
    public void ADayWindowCoversTwentyFourHours()
    {
        var start = LocalTime.StartOfDayUtc(new DateOnly(2026, 9, 1));
        var end = LocalTime.EndOfDayUtc(new DateOnly(2026, 9, 1));

        Assert.Equal(24, (end - start).TotalHours);
    }

    [Theory]
    [InlineData(6, "waking")]
    [InlineData(10, "morning")]
    [InlineData(14, "afternoon")]
    [InlineData(19, "evening")]
    [InlineData(23, "night")]
    [InlineData(2, "night")]
    public void ReadingsAreBucketedByTimeOfDay(int hour, string expected)
        => Assert.Equal(expected, LocalTime.TimeOfDayBucket(new DateTime(2026, 9, 1, hour, 0, 0)));

    [Fact]
    public void TodayAndYesterdayAreOneDayApart()
        => Assert.Equal(1, LocalTime.Today.DayNumber - LocalTime.Yesterday.DayNumber);
}
