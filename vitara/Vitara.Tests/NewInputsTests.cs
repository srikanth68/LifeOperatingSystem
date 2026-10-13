using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// Strength, iron studies, hs-CRP, the three habit logs and sleep timing (longevity brief
// items 6 and 8).
public class NewInputsTests
{
    private static readonly string[] NewKeys =
    [
        MetricKeys.GripStrengthKg, MetricKeys.SitToStand30s, MetricKeys.PushupsMax, MetricKeys.Strength1RmKg,
        MetricKeys.VisceralFat, MetricKeys.HsCrp, MetricKeys.Iron, MetricKeys.Tibc, MetricKeys.TransferrinSaturation,
        MetricKeys.AlcoholUnits, MetricKeys.CaffeineLastTime, MetricKeys.LastMealTime,
        MetricCatalogue.CaffeineHoursBeforeBed, MetricCatalogue.LastMealHoursBeforeBed,
        MetricKeys.BedtimeMinutes, MetricKeys.WakeMinutes, MetricCatalogue.BedtimeVariabilityMinutes,
    ];

    [Fact]
    public void EveryNewKeyIsCataloguedGradedAndHasADirection()
    {
        foreach (var key in NewKeys)
        {
            Assert.NotNull(MetricCatalogue.Find(key));
            Assert.NotNull(Evidence.GradeFor(key));
            // Known, even where the answer is "neither way is better".
            Assert.True(MetricDirection.IsKnown(key), $"{key} has no declared direction");
        }
    }

    [Fact]
    public void GripSplitsByHand_AndAOneRepMaxByExercise()
    {
        Assert.Equal("hand=left", BaselineKeys.Signature(MetricKeys.GripStrengthKg, new MeasurementContext(Hand: "left")));
        Assert.Equal("exercise=squat", BaselineKeys.Signature(MetricKeys.Strength1RmKg, new MeasurementContext(Exercise: "Squat")));
        Assert.False(BaselineKeys.CanBaseline(MetricKeys.Strength1RmKg, new MeasurementContext()));
    }

    [Fact]
    public async Task AnImplausibleReadingIsRefused()
    {
        var ctrl = new MeasurementsController(new FakeRepo(), Microsoft.Extensions.Logging.Abstractions.NullLogger<MeasurementsController>.Instance);
        var result = await ctrl.Add(new MeasurementRequest(MetricKeys.GripStrengthKg, 400, null, null, null, Hand: "left"));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void TheFormOffersAlcoholAtZero_AndClockTimesAsTimes()
    {
        var ok = Assert.IsType<OkObjectResult>(new MeasurementsController(new FakeRepo(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MeasurementsController>.Instance).Metrics());
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"metric\":\"alcohol_units\"", json);
        Assert.Contains("\"defaultValue\":0", json);
        Assert.Contains("\"input\":\"time\"", json);
    }

    // ── Hours before bed ────────────────────────────────────────────────────────

    private static readonly DateOnly Friday = new(2026, 9, 4);

    [Fact]
    public void CaffeineAtFourBeforeAnElevenThirtyBedtimeIsSevenAndAHalfHours()
    {
        var bed = new Dictionary<DateOnly, double> { [Friday.AddDays(1)] = -30 };   // 23:30
        var hours = HabitTiming.HoursBeforeBed([(Friday, 16 * 60)], bed);
        Assert.Equal(7.5, Assert.Single(hours).Hours);
    }

    [Fact]
    public void BothSidesOfMidnightAreHandled()
    {
        // Bed at 01:00, last coffee logged as 00:15 -- the small hours of the same evening.
        var bed = new Dictionary<DateOnly, double> { [Friday.AddDays(1)] = 60 };
        var hours = HabitTiming.HoursBeforeBed([(Friday, 15)], bed);
        Assert.Equal(0.75, Assert.Single(hours).Hours);
    }

    [Fact]
    public void ATravelDayIsRefusedRatherThanComputed()
    {
        // The log is in one clock and the bedtime perhaps in another; the difference
        // between them would be the time difference, not the habit.
        var bed = new Dictionary<DateOnly, double> { [Friday.AddDays(1)] = -30 };
        var hours = HabitTiming.HoursBeforeBed([(Friday, 16 * 60)], bed, [(Friday, Friday.AddDays(3))]);
        Assert.Empty(hours);
    }

    // ── Sleep timing ────────────────────────────────────────────────────────────

    [Fact]
    public void BedtimesEitherSideOfMidnightAverageToMidnight_NotNoon()
    {
        var at2330 = ObservationProjector.AroundMidnight(new DateTime(2026, 9, 4, 23, 30, 0));
        var at0030 = ObservationProjector.AroundMidnight(new DateTime(2026, 9, 5, 0, 30, 0));

        Assert.Equal(-30, at2330);
        Assert.Equal(30, at0030);
        Assert.Equal(0, HabitTiming.CircularMeanMinutes([at2330, at0030]));
        Assert.InRange(HabitTiming.CircularSdMinutes([at2330, at0030]), 29, 31);
    }

    [Fact]
    public void TheNightsBedtimeIsProjected_ButANapsIsNot()
    {
        var night = new SleepSession
        {
            Id = "n", Day = Friday, Type = "long_sleep", TotalSleepMinutes = 420,
            BedtimeStart = new DateTime(2026, 9, 3, 23, 40, 0), BedtimeEnd = new DateTime(2026, 9, 4, 7, 10, 0),
        };
        var nap = new SleepSession
        {
            Id = "p", Day = Friday.AddDays(1), Type = "late_nap", TotalSleepMinutes = 40,
            BedtimeStart = new DateTime(2026, 9, 5, 15, 0, 0), BedtimeEnd = new DateTime(2026, 9, 5, 15, 45, 0),
        };

        var fromNight = ObservationProjector.FromSleep([night]);
        Assert.Equal(-20, fromNight.Single(o => o.Metric == MetricKeys.BedtimeMinutes).Value);
        Assert.Equal(430, fromNight.Single(o => o.Metric == MetricKeys.WakeMinutes).Value);

        Assert.DoesNotContain(ObservationProjector.FromSleep([nap]), o => o.Metric == MetricKeys.BedtimeMinutes);
    }

    [Fact]
    public void TravelNightsAreLeftOutOfTheBedtimeBaseline()
    {
        var asOf = new DateOnly(2026, 9, 30);
        var obs = Enumerable.Range(0, 40).Select(i => new Observation
        {
            Metric = MetricKeys.BedtimeMinutes,
            ObservedDateLocal = asOf.AddDays(-i),
            ObservedAtLocal = asOf.AddDays(-i).ToDateTime(new TimeOnly(7, 0)),
            Value = i < 5 ? 180 : -30,   // five nights abroad going to bed at 3am
            EligibleForBaseline = true,
        }).ToList();
        var trip = new List<TravelPeriod> { new() { StartLocal = asOf.AddDays(-4), EndLocal = asOf, AwayTz = "Asia/Kolkata" } };

        var baseline = BaselineCalculator.Compute(MetricKeys.BedtimeMinutes, "", asOf,
            new BaselineInputs(obs, [], trip, []), 60, 21, 1.5, 14);

        Assert.Equal(-30, baseline.Median);
        Assert.Contains("travel", baseline.ExclusionsJson);
    }

    // ── Correlations: an unlogged habit cannot raise the bar ────────────────────

    [Fact]
    public void ADriverWithTwelveDaysChangesNothingForTheOtherPairs()
    {
        var asOf = new DateOnly(2026, 9, 30);
        var rng = new Random(5);
        var obs = new List<Observation>();
        for (var i = 0; i < 90; i++)
        {
            var day = asOf.AddDays(-i);
            var load = 300 + rng.Next(0, 400);
            obs.Add(Obs(MetricKeys.ActiveCalories, day, load));
            // Tomorrow's HRV falls with today's load, plus noise.
            obs.Add(Obs(MetricKeys.HrvRmssd, day.AddDays(1), 70 - load / 20.0 + rng.NextDouble() * 4));
        }

        var without = Correlations.Run(obs, asOf);

        var withAlcohol = obs.Concat(Enumerable.Range(0, 12)
            .Select(i => Obs(MetricKeys.AlcoholUnits, asOf.AddDays(-i), i % 3))).ToList();
        var with = Correlations.Run(withAlcohol, asOf);

        Assert.NotEmpty(without);
        Assert.Equal(without.Select(c => (c.Driver, c.Outcome, c.LagDays, c.PValue)),
                     with.Select(c => (c.Driver, c.Outcome, c.LagDays, c.PValue)));
        Assert.DoesNotContain(with, c => c.Driver == MetricKeys.AlcoholUnits);
    }

    private static Observation Obs(string metric, DateOnly day, double value) => new()
    {
        Metric = metric,
        ObservedDateLocal = day,
        ObservedAtLocal = day.ToDateTime(new TimeOnly(7, 0)),
        Value = value,
    };
}
