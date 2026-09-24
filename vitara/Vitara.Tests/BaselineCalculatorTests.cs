using Vitara.Insight.Health;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// Projecting typed rows into flat observations. The failure that matters is a missing
// value becoming a zero: a night the ring was not worn is not a night of zero HRV, and
// a baseline that averages those in describes someone other than the user.
public class ObservationProjectorTests
{
    private static SleepSession Night(double? hrv = 48, double? temp = -0.1, int? score = 82) => new()
    {
        Id = "s1",
        Day = new DateOnly(2026, 9, 1),
        BedtimeEnd = new DateTime(2026, 9, 1, 7, 12, 0),
        TotalSleepMinutes = 430, DeepMinutes = 80, RemMinutes = 95,
        AvgHrv = hrv, SkinTempDeviation = temp, Score = score, LowestHr = 51,
    };

    [Fact]
    public void SkinTemperatureIsProjectedAsAFirstClassMetric()
    {
        // Combined with resting HR and HRV it is the earliest illness signal available
        // here, and it is easy to leave behind as an incidental field on a sleep row.
        var metrics = ObservationProjector.FromSleep([Night()]).Select(o => o.Metric).ToList();
        Assert.Contains(MetricKeys.SkinTempDeviation, metrics);
    }

    [Fact]
    public void MissingOptionalValuesAreOmittedNotZeroed()
    {
        var metrics = ObservationProjector.FromSleep([Night(hrv: null, temp: null, score: null)])
            .Select(o => o.Metric).ToList();

        Assert.DoesNotContain(MetricKeys.HrvRmssd, metrics);
        Assert.DoesNotContain(MetricKeys.SkinTempDeviation, metrics);
        Assert.DoesNotContain(MetricKeys.SleepScore, metrics);
        // The durations are non-nullable and always present.
        Assert.Contains(MetricKeys.TotalSleepMinutes, metrics);
    }

    [Fact]
    public void RestingHeartRateIsProjectedFromExactlyOnePlace()
    {
        // Oura reports it on both sleep and readiness. Two sources of one truth either
        // collide on the unique index or silently double-count in a baseline.
        var fromSleep = ObservationProjector.FromSleep([Night()]).Count(o => o.Metric == MetricKeys.RestingHeartRate);
        var fromReadiness = ObservationProjector
            .FromReadiness(new DailyReadiness { Id = "r1", Day = new DateOnly(2026, 9, 1), Score = 74, RestingHeartRate = 51 })
            .Count(o => o.Metric == MetricKeys.RestingHeartRate);

        Assert.Equal(1, fromSleep);
        Assert.Equal(0, fromReadiness);
    }

    [Fact]
    public void TheNightKeepsOurasOwnLocalDay()
    {
        // Re-deriving the day from a timestamp is how sleep beginning at 11pm ends up
        // filed under tomorrow. Oura already reports it in the user's terms.
        Assert.All(ObservationProjector.FromSleep([Night()]),
            o => Assert.Equal(new DateOnly(2026, 9, 1), o.ObservedDateLocal));
    }

    [Fact]
    public void OuraMetricsCarryNoContextSignature()
        => Assert.All(ObservationProjector.FromSleep([Night()]), o => Assert.Equal("", o.BaselineSignature));

    [Fact]
    public void AWeighInIsMediumTierAndBucketedByTimeOfDay()
    {
        var observation = Assert.Single(ObservationProjector.FromWeighIn(
            new WeighIn { Id = "2026-09-01", Day = new DateOnly(2026, 9, 1), WeightKg = 82.4 }));

        Assert.Equal(Tiers.Medium, observation.Tier);
        Assert.Equal("manual", observation.Source);
        Assert.Contains("timeOfDay=", observation.BaselineSignature);
        Assert.True(observation.EligibleForBaseline);
    }
}

// What normal is, and what was left out of deciding it. The exclusions are the whole
// point: a baseline that quietly absorbs two weeks of illness stops flagging illness,
// while every number it reports still looks reasonable.
public class BaselineCalculatorTests
{
    private const string Metric = MetricKeys.RestingHeartRate;
    private static readonly DateOnly AsOf = new(2026, 9, 1);

    private static List<Observation> Series(int days, double value, DateOnly? endingOn = null)
    {
        var end = endingOn ?? AsOf;
        return Enumerable.Range(0, days).Select(i => new Observation
        {
            Metric = Metric,
            Value = value,
            ObservedDateLocal = end.AddDays(-i),
            ObservedAtLocal = end.AddDays(-i).ToDateTime(new TimeOnly(7, 0)),
            EligibleForBaseline = true,
        }).ToList();
    }

    private static Baseline Compute(BaselineInputs inputs, int minN = 21) =>
        BaselineCalculator.Compute(Metric, "", AsOf, inputs, windowDays: 60, minN: minN,
            regimeShiftZ: 1.5, regimeDwellDays: 14);

    private static BaselineInputs Inputs(List<Observation> obs,
        List<ExcludedPeriod>? excluded = null, List<TravelPeriod>? travel = null, List<Device>? devices = null,
        List<Intervention>? interventions = null)
        => new(obs, excluded ?? [], travel ?? [], devices ?? [], interventions ?? []);

    [Fact]
    public void AThinWindowIsMarkedInvalidRatherThanReportedAsFact()
    {
        // Exposed as a flag, not suppressed: "not enough data yet" is true and useful,
        // where a mean of four readings gets acted on as though it meant something.
        var baseline = Compute(Inputs(Series(5, 52)));

        Assert.False(baseline.IsValid);
        Assert.Equal(5, baseline.N);
    }

    [Fact]
    public void AFullWindowIsValid()
    {
        var baseline = Compute(Inputs(Series(40, 52)));
        Assert.True(baseline.IsValid);
    }

    [Fact]
    public void IllnessIsLeftOutOfWhatCountsAsNormal()
    {
        // Ten days of illness at 62 inside a window otherwise at 52. Absorbed, it lifts
        // the baseline and the system stops flagging the next episode.
        var observations = Series(40, 52);
        foreach (var o in observations.Where(o => o.ObservedDateLocal >= AsOf.AddDays(-9)))
            o.Value = 62;

        var excluded = new List<ExcludedPeriod>
        {
            new() { StartLocal = AsOf.AddDays(-9), EndLocal = AsOf, Reason = "illness", ExcludeFromBaseline = true },
        };

        var baseline = Compute(Inputs(observations, excluded: excluded));

        Assert.InRange(baseline.Mean, 51.5, 52.5);
        Assert.Contains("excluded_period", baseline.ExclusionsJson);
    }

    [Fact]
    public void TravelOnlyExcludesCircadianMetrics()
    {
        // Sleep timing abroad is a different question, not a worse night. Steps abroad
        // are just steps.
        var travel = new List<TravelPeriod>
        {
            new() { StartLocal = AsOf.AddDays(-9), EndLocal = AsOf, AwayTz = "Asia/Kolkata" },
        };

        var sleep = BaselineCalculator.Compute(MetricKeys.TotalSleepMinutes, "", AsOf,
            Inputs(Series(40, 430).Select(o => { o.Metric = MetricKeys.TotalSleepMinutes; return o; }).ToList(), travel: travel),
            60, 21, 1.5, 14);

        var steps = BaselineCalculator.Compute(MetricKeys.Steps, "", AsOf,
            Inputs(Series(40, 9000).Select(o => { o.Metric = MetricKeys.Steps; return o; }).ToList(), travel: travel),
            60, 21, 1.5, 14);

        Assert.Contains("travel", sleep.ExclusionsJson);
        Assert.Null(steps.ExclusionsJson);
    }

    [Fact]
    public void ANewRingIsADiscontinuityNotAHealthEvent()
    {
        // Absolute values shift when the hardware does. A window spanning the swap
        // averages two instruments together and reports the swap as a change in the
        // user.
        var observations = Series(40, 52);
        foreach (var o in observations.Where(o => o.ObservedDateLocal >= AsOf.AddDays(-9)))
            o.Value = 56;

        var devices = new List<Device>
        {
            new() { Kind = "ring", Model = "Gen3", ActiveFromLocal = AsOf.AddDays(-60) },
            new() { Kind = "ring", Model = "Gen4", ActiveFromLocal = AsOf.AddDays(-9) },
        };

        var baseline = Compute(Inputs(observations, devices: devices), minN: 5);

        Assert.InRange(baseline.Mean, 55.5, 56.5);   // only the new ring's readings
        Assert.Contains("pre_device_change", baseline.ExclusionsJson);
    }

    [Fact]
    public void ReadingsMissingRequiredContextDoNotFeedTheBaseline()
    {
        var observations = Series(40, 52);
        foreach (var o in observations.Take(10)) o.EligibleForBaseline = false;

        var baseline = Compute(Inputs(observations));

        Assert.Equal(30, baseline.N);
        Assert.Contains("missing_context", baseline.ExclusionsJson);
    }

    [Fact]
    public void AnExplainedStepResetsTheWindowToTheNewLevel()
    {
        // Thirty days at 52, then twenty at 57, with a medication started when it moved.
        // The baseline should describe 57 and record where the change was.
        var observations = Series(20, 57).Concat(Series(30, 52, AsOf.AddDays(-20))).ToList();
        var started = new List<Intervention>
        {
            new() { Kind = "medication", Name = "beta blocker", StartedOnLocal = AsOf.AddDays(-19) },
        };

        var baseline = Compute(Inputs(observations, interventions: started), minN: 5);

        Assert.NotNull(baseline.RegimeStartLocal);
        Assert.InRange(baseline.Mean, 56.5, 57.5);
    }

    [Fact]
    public void AStepTheHarmlessWayIsAdoptedWithoutNeedingAnExplanation()
    {
        // Resting heart rate DROPPING is not the failure mode this guards against:
        // nothing is hidden by calling a better number normal.
        var observations = Series(20, 47).Concat(Series(30, 52, AsOf.AddDays(-20))).ToList();
        var baseline = Compute(Inputs(observations), minN: 5);

        Assert.NotNull(baseline.RegimeStartLocal);
        Assert.InRange(baseline.Mean, 46.5, 47.5);
    }

    [Fact]
    public void AnUnexplainedStepTheWrongWayIsNotAdoptedAsNormal()
    {
        // The review's sharpest point, and the reason adoption is now arbitrated: a
        // slow decline is a sustained step change, and rebuilding the baseline around
        // it makes every later deviation check agree that nothing is wrong.
        var observations = Series(20, 57).Concat(Series(30, 52, AsOf.AddDays(-20))).ToList();

        var baseline = Compute(Inputs(observations), minN: 5);

        Assert.Null(baseline.RegimeStartLocal);
        Assert.Contains("unadopted_regime_shift", baseline.ExclusionsJson);
        // Still describing the person before the shift, so 57 keeps reading as high.
        Assert.InRange(baseline.Median, 51.5, 54.5);
    }

    [Fact]
    public void AnUnknownMetricsStepIsAdopted_BecauseNobodyKnowsWhichWayIsBad()
    {
        // A lab analyte nobody enumerated has no polarity. Guessing at one would be
        // worse than treating the change as ordinary.
        var observations = Series(20, 57).Concat(Series(30, 52, AsOf.AddDays(-20))).ToList();
        foreach (var o in observations) o.Metric = "some_new_analyte";

        var baseline = BaselineCalculator.Compute("some_new_analyte", "", AsOf, Inputs(observations),
            windowDays: 60, minN: 5, regimeShiftZ: 1.5, regimeDwellDays: 14);

        Assert.NotNull(baseline.RegimeStartLocal);
    }

    [Fact]
    public void AnEmptyWindowProducesAnInvalidBaselineRatherThanThrowing()
    {
        var baseline = Compute(Inputs([]));
        Assert.False(baseline.IsValid);
        Assert.Equal(0, baseline.N);
    }
}
