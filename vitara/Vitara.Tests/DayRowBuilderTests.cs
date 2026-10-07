using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Insight.Health;

namespace Vitara.Tests;

// The forecast and the self-check see a person through these rows.
//
// The defect these pin down: the forecast read only the typed tables (what the Oura sync
// and the phone's live push write), while an Apple Health export file writes only
// Observations. Someone whose history arrived as an export got a forecast built on
// nothing and no hint that anything was missing.
public class DayRowBuilderTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    private static Observation Obs(string metric, double value, string source = "apple_health", DateOnly? day = null) => new()
    {
        Metric = metric,
        Value = value,
        Source = source,
        ObservedDateLocal = day ?? Day,
        ObservedAtLocal = (day ?? Day).ToDateTime(new TimeOnly(7, 0)),
    };

    private static List<Prediction.DayRow> Build(
        IEnumerable<Observation>? observations = null,
        IEnumerable<SleepSession>? sleep = null,
        IEnumerable<DailyReadiness>? readiness = null,
        IEnumerable<DailyActivity>? activity = null) =>
        DayRowBuilder.Build(
            (readiness ?? []).ToList(), (activity ?? []).ToList(), (sleep ?? []).ToList(),
            (observations ?? []).ToList(), Day, Day);

    // ── The bug ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAppleHealthExportWithNoTypedRowsStillProducesADay()
    {
        var row = Build(observations:
        [
            Obs(MetricKeys.RestingHeartRate, 58),
            Obs(MetricKeys.HrvRmssd, 41),
            Obs(MetricKeys.TotalSleepMinutes, 415),
            Obs(MetricKeys.ActiveCalories, 520),
        ]).Single();

        Assert.Equal(58, row.RestingHr);
        Assert.Equal(41, row.Hrv);
        Assert.Equal(415, row.SleepMinutes);
        Assert.Equal(520, row.ActiveCalories);
    }

    // ── Typed first: nobody who already works changes ───────────────────────────

    [Fact]
    public void ATypedValueWinsOverAnObservationOfTheSameThing()
    {
        var row = Build(
            sleep: [new SleepSession { Id = "s", Day = Day, TotalSleepMinutes = 430, LowestHr = 52, AvgHrv = 60 }],
            observations: [Obs(MetricKeys.RestingHeartRate, 70), Obs(MetricKeys.TotalSleepMinutes, 300)]).Single();

        Assert.Equal(52, row.RestingHr);
        Assert.Equal(430, row.SleepMinutes);
    }

    [Fact]
    public void OuraNightsAreNotCountedTwiceBecauseTheSyncAlsoProjectsThemIntoObservations()
    {
        var night = new SleepSession { Id = "s", Day = Day, TotalSleepMinutes = 430, LowestHr = 52, AvgHrv = 60 };

        var typedOnly = Build(sleep: [night]).Single();
        var withProjection = Build(sleep: [night], observations: ObservationProjector.FromSleep([night])).Single();

        Assert.Equal(typedOnly, withProjection);
    }

    // ── Holes are filled field by field ─────────────────────────────────────────

    [Fact]
    public void AMissingFieldIsFilledWithoutDisturbingTheOnesThatArePresent()
    {
        // The phone pushed sleep; the export has the heart rate for the same night.
        var row = Build(
            sleep: [new SleepSession { Id = "s", Day = Day, TotalSleepMinutes = 400, LowestHr = null, AvgHrv = null }],
            observations: [Obs(MetricKeys.RestingHeartRate, 57)]).Single();

        Assert.Equal(400, row.SleepMinutes);
        Assert.Equal(57, row.RestingHr);
        Assert.Null(row.Hrv);
    }

    [Fact]
    public void NothingAnywhereIsStillARowOfNulls()
    {
        var row = Build().Single();

        Assert.Equal(Day, row.Day);
        Assert.Null(row.Readiness);
        Assert.Null(row.RestingHr);
        Assert.Null(row.Hrv);
        Assert.Null(row.SleepMinutes);
        Assert.Null(row.ActiveCalories);
    }

    [Fact]
    public void ReadinessHasNoAppleEquivalentSoItStaysEmptyRatherThanBeingInvented()
    {
        var row = Build(observations: [Obs(MetricKeys.RestingHeartRate, 58)]).Single();

        Assert.Null(row.Readiness);
    }

    // ── Two sources for one night ───────────────────────────────────────────────

    [Fact]
    public void TwoDevicesAreNeverBlended()
    {
        var row = Build(observations:
        [
            Obs(MetricKeys.TotalSleepMinutes, 400, source: "apple_health"),
            Obs(MetricKeys.TotalSleepMinutes, 480, source: "oura"),
        ]).Single();

        Assert.Equal(480, row.SleepMinutes);   // Oura preferred, not 440
    }

    [Fact]
    public void ARepeatedDailyTotalIsTakenOnceNotAddedUp()
    {
        var row = Build(observations:
        [
            Obs(MetricKeys.ActiveCalories, 500),
            Obs(MetricKeys.ActiveCalories, 500),
        ]).Single();

        Assert.Equal(500, row.ActiveCalories);
    }

    [Fact]
    public void ARateWithSeveralSamplesIsAveraged()
    {
        var row = Build(observations: [Obs(MetricKeys.HrvRmssd, 40), Obs(MetricKeys.HrvRmssd, 50)]).Single();

        Assert.Equal(45, row.Hrv);
    }

    [Fact]
    public void ADayOutsideTheWindowIsIgnored()
    {
        var row = Build(observations: [Obs(MetricKeys.RestingHeartRate, 99, day: Day.AddDays(-1))]).Single();

        Assert.Null(row.RestingHr);
    }

    // ── Every day in the window is a row ────────────────────────────────────────

    [Fact]
    public void AGapStaysAGapInsteadOfClosingUp()
    {
        var rows = DayRowBuilder.Build([], [], [],
            [Obs(MetricKeys.RestingHeartRate, 58, day: Day), Obs(MetricKeys.RestingHeartRate, 60, day: Day.AddDays(3))],
            Day, Day.AddDays(3));

        Assert.Equal(4, rows.Count);
        Assert.Equal(58, rows[0].RestingHr);
        Assert.Null(rows[1].RestingHr);
        Assert.Null(rows[2].RestingHr);
        Assert.Equal(60, rows[3].RestingHr);
    }
}
