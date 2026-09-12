using System.Text;
using Vitara.Application;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// Reading Apple Health's own export.
//
// The fixtures below are shaped like the real file — a 810MB export with 1.7 million
// records, written by nineteen different apps, in imperial units throughout.
//
// What is pinned down here is mostly the silent failures. A rejected file is visible
// and fixable; a file that imports cleanly with weight in pounds stored as kilograms,
// or Apple's SDNN written into a field holding Oura's RMSSD, is a plausible-looking
// number that poisons every baseline built on it.
public class HealthXmlImportTests
{
    private static XmlScanResult Scan(string body)
    {
        var xml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <HealthData locale="en_US">
        {body}
        </HealthData>
        """;

        return HealthXmlImport.Scan(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    }

    private static double Value(XmlScanResult r, string metric) =>
        r.Daily.Single(d => d.Metric == metric).Value;

    // ── The basics ──────────────────────────────────────────────────────────────

    [Fact]
    public void StepsAcrossADaySum()
    {
        // 299,690 step records in the real export. They are per-interval, not per-day.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="4000"/>
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-01 18:00:00 -0400" endDate="2026-09-01 19:00:00 -0400" value="4431"/>
        """);

        Assert.Equal(8431, Value(r, MetricKeys.Steps));
    }

    [Fact]
    public void RestingHeartRateIsAveragedNotSummed()
    {
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierRestingHeartRate" sourceName="Kanth" unit="count/min" startDate="2026-09-01 07:00:00 -0400" endDate="2026-09-01 07:00:00 -0400" value="52"/>
        <Record type="HKQuantityTypeIdentifierRestingHeartRate" sourceName="Kanth" unit="count/min" startDate="2026-09-01 20:00:00 -0400" endDate="2026-09-01 20:00:00 -0400" value="56"/>
        """);

        Assert.Equal(54, Value(r, MetricKeys.RestingHeartRate));
    }

    // ── Imperial units: the silent-corruption class ─────────────────────────────

    [Fact]
    public void PoundsBecomeKilograms()
    {
        // The real export carries 158 body-mass records in lb. Stored as kilograms,
        // 176.4 is a number that looks entirely reasonable.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierBodyMass" sourceName="Withings" unit="lb" startDate="2026-09-01 07:00:00 -0400" endDate="2026-09-01 07:00:00 -0400" value="176.4"/>
        """);

        Assert.Equal(80.0, Value(r, MetricKeys.WeightKg), 1);
    }

    [Fact]
    public void KilogramsPassThrough()
    {
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierBodyMass" sourceName="Withings" unit="kg" startDate="2026-09-01 07:00:00 -0400" endDate="2026-09-01 07:00:00 -0400" value="80"/>
        """);

        Assert.Equal(80.0, Value(r, MetricKeys.WeightKg), 1);
    }

    [Fact]
    public void OxygenSaturationIsAFractionAndBecomesAPercentage()
    {
        // Apple writes 0.97 with a "%" unit attached. Stored raw it sits two orders of
        // magnitude below its own baseline.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierOxygenSaturation" sourceName="Oura" unit="%" startDate="2026-09-01 03:00:00 -0400" endDate="2026-09-01 03:00:00 -0400" value="0.97"/>
        """);

        Assert.Equal(97, Value(r, MetricKeys.Spo2Average), 1);
    }

    [Fact]
    public void AlreadyPercentageValuesAreNotMultipliedAgain()
        => Assert.Equal(97, HealthXmlImport.Convert(MetricKeys.Spo2Average, 97, "%"), 1);

    [Fact]
    public void HeightInInchesBecomesMetres()
    {
        // The field BMI and waist-to-height both need, and it was empty on the profile.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierHeight" sourceName="Health" unit="in" startDate="2020-01-01 00:00:00 -0500" endDate="2020-01-01 00:00:00 -0500" value="70"/>
        """);

        Assert.NotNull(r.HeightMetres);
        Assert.Equal(1.778, r.HeightMetres!.Value, 3);
    }

    // ── HRV: SDNN is not RMSSD ──────────────────────────────────────────────────

    [Fact]
    public void AppleHrvGetsItsOwnMetric()
    {
        // Apple records SDNN, Oura records RMSSD. Different statistics over the same
        // intervals. Pooling them produces a baseline describing neither.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierHeartRateVariabilitySDNN" sourceName="Kanth" unit="ms" startDate="2026-09-01 03:00:00 -0400" endDate="2026-09-01 03:00:00 -0400" value="42"/>
        """);

        Assert.Equal(HealthXmlImport.HrvSdnn, r.Daily.Single().Metric);
        Assert.DoesNotContain(r.Daily, d => d.Metric == MetricKeys.HrvRmssd);
    }

    // ── Sleep: spans, not values ────────────────────────────────────────────────

    [Fact]
    public void AsleepSpansSumIntoANight()
    {
        // A SleepAnalysis record has no number on it at all — it is a start, an end and
        // a stage. 240 + 90 + 60 minutes.
        var r = Scan("""
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Oura" value="HKCategoryValueSleepAnalysisAsleepCore" startDate="2026-09-01 23:00:00 -0400" endDate="2026-09-02 03:00:00 -0400"/>
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Oura" value="HKCategoryValueSleepAnalysisAsleepDeep" startDate="2026-09-02 03:00:00 -0400" endDate="2026-09-02 04:30:00 -0400"/>
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Oura" value="HKCategoryValueSleepAnalysisAsleepREM" startDate="2026-09-02 04:30:00 -0400" endDate="2026-09-02 05:30:00 -0400"/>
        """);

        Assert.Equal(390, Value(r, MetricKeys.TotalSleepMinutes));
    }

    [Fact]
    public void InBedIsNotSleep()
    {
        // Counting time in bed as sleep is how a restless night becomes a long one.
        var r = Scan("""
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Oura" value="HKCategoryValueSleepAnalysisInBed" startDate="2026-09-01 22:00:00 -0400" endDate="2026-09-02 07:00:00 -0400"/>
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Oura" value="HKCategoryValueSleepAnalysisAsleepCore" startDate="2026-09-01 23:00:00 -0400" endDate="2026-09-02 05:00:00 -0400"/>
        """);

        Assert.Equal(360, Value(r, MetricKeys.TotalSleepMinutes));
    }

    [Fact]
    public void TheNightBelongsToTheDayYouWokeUp()
    {
        // Sleep starting at 11pm on the 1st is the night OF the 2nd. Filing it under the
        // start date puts every night on the wrong day.
        var r = Scan("""
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Oura" value="HKCategoryValueSleepAnalysisAsleepCore" startDate="2026-09-01 23:00:00 -0400" endDate="2026-09-02 06:00:00 -0400"/>
        """);

        Assert.Equal(new DateOnly(2026, 9, 2), r.Daily.Single().Day);
    }

    // ── Sources ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EachAppIsCountedSeparately()
    {
        // Nineteen writers in the real export. Keeping them apart is what makes the
        // picker possible at all.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Oura" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="100"/>
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="200"/>
        """);

        Assert.Equal(2, r.Sources.Count);
        Assert.Equal(2, r.Daily.Count);
    }

    [Fact]
    public void OuraIsRecommendedOff()
    {
        // It reaches Vitara through its own API with sleep stages, RMSSD and a
        // temperature deviation Apple never receives. Importing the Apple copy would
        // overwrite the better record with a coarser version of itself.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Oura" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="100"/>
        """);

        Assert.True(r.Sources.Single().RecommendedOff);
    }

    [Fact]
    public void EverythingElseIsRecommendedOn()
    {
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Srikanth&#8217;s Apple Watch" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="100"/>
        """);

        Assert.False(r.Sources.Single().RecommendedOff);
    }

    // ── What is deliberately skipped ────────────────────────────────────────────

    [Fact]
    public void RawHeartRateIsNotImported()
    {
        // 461,357 records in the real export. Vitara does not project raw heart rate at
        // all -- it is the one series that would fill the disk on a box also hosting the
        // model -- so importing it would break that decision from the other end.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierHeartRate" sourceName="Oura" unit="count/min" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 08:00:00 -0400" value="72"/>
        """);

        Assert.Empty(r.Daily);
        Assert.Contains(r.IgnoredTypes, t => t.StartsWith("HeartRate"));
    }

    [Fact]
    public void SkippedTypesAreReportedWithCounts()
    {
        // A real export ignores three dozen types. Saying which, and how many, is the
        // difference between "nothing imported" and knowing why.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierWalkingSpeed" sourceName="Kanth" unit="mi/hr" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 08:00:00 -0400" value="3"/>
        <Record type="HKQuantityTypeIdentifierWalkingSpeed" sourceName="Kanth" unit="mi/hr" startDate="2026-09-02 08:00:00 -0400" endDate="2026-09-02 08:00:00 -0400" value="3"/>
        """);

        Assert.Contains(r.IgnoredTypes, t => t.Contains("WalkingSpeed") && t.Contains("2"));
    }

    // ── Robustness ──────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnreadableDateIsSkippedNotGuessed()
    {
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="whenever" endDate="whenever" value="500"/>
        """);

        Assert.Empty(r.Daily);
        Assert.Contains(r.Warnings, w => w.Contains("could not be read"));
    }

    [Fact]
    public void AnEmptyExportSaysSoRatherThanThrowing()
    {
        var r = Scan("");

        Assert.Empty(r.Daily);
        Assert.Null(r.FirstDay);
    }

    [Fact]
    public void TheDateRangeIsReported()
    {
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2019-03-04 08:00:00 -0500" endDate="2019-03-04 09:00:00 -0500" value="100"/>
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="200"/>
        """);

        Assert.Equal(new DateOnly(2019, 3, 4), r.FirstDay);
        Assert.Equal(new DateOnly(2026, 9, 1), r.LastDay);
    }

    // ── Plausibility: found only by running the real file ───────────────────────

    [Fact]
    public void AHeartRateOf176IsNotARestingHeartRate()
    {
        // Straight out of the 810MB export. Some app wrote a sprint into the resting
        // field, and one such value in a baseline is enough to move it.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierRestingHeartRate" sourceName="Kanth" unit="count/min" startDate="2026-09-01 07:00:00 -0400" endDate="2026-09-01 07:00:00 -0400" value="176"/>
        """);

        Assert.Empty(r.Daily);
        Assert.Contains(r.Warnings, w => w.Contains("resting hr") && w.Contains("implausible"));
    }

    [Fact]
    public void AHalfMinuteNightIsNotANight()
    {
        // Also real: a day whose summed sleep came to 0.5 minutes. Exactly the shape of
        // the Oura nap bug -- a fragment counted as a night, wrecking the baseline it
        // joins and manufacturing a night's sleep debt.
        var r = Scan("""
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Pillow" value="HKCategoryValueSleepAnalysisAsleepCore" startDate="2026-09-01 23:00:00 -0400" endDate="2026-09-01 23:00:30 -0400"/>
        """);

        Assert.Empty(r.Daily);
    }

    [Fact]
    public void FifteenHoursOfSleepIsAStuckSpan()
    {
        var r = Scan("""
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Pillow" value="HKCategoryValueSleepAnalysisAsleepCore" startDate="2026-09-01 14:00:00 -0400" endDate="2026-09-02 05:30:00 -0400"/>
        """);

        Assert.Empty(r.Daily);
    }

    [Fact]
    public void ADayWithOneStepIsAPhoneOnATable()
    {
        // A fact about the phone, not about the user, and it drags the steps baseline
        // down every time it appears.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="1"/>
        """);

        Assert.Empty(r.Daily);
    }

    [Fact]
    public void PerfectlyOrdinaryReadingsSurvive()
    {
        // The bounds exist to exclude values that cannot be a measurement of this thing
        // at all -- NOT unusual readings, which are the interesting ones.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierRestingHeartRate" sourceName="Kanth" unit="count/min" startDate="2026-09-01 07:00:00 -0400" endDate="2026-09-01 07:00:00 -0400" value="91"/>
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="21619"/>
        """);

        Assert.Equal(2, r.Daily.Count);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void BoundsApplyToTheDayTotalNotTheRecord()
    {
        // A 30-second span is not implausible on its own. It is implausible as a night,
        // and the day's total is what claims to be one -- so three short spans that sum
        // to a real night are kept.
        var r = Scan("""
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Pillow" value="HKCategoryValueSleepAnalysisAsleepCore" startDate="2026-09-01 23:00:00 -0400" endDate="2026-09-02 02:00:00 -0400"/>
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Pillow" value="HKCategoryValueSleepAnalysisAsleepREM" startDate="2026-09-02 02:00:00 -0400" endDate="2026-09-02 04:00:00 -0400"/>
        <Record type="HKCategoryTypeIdentifierSleepAnalysis" sourceName="Pillow" value="HKCategoryValueSleepAnalysisAsleepDeep" startDate="2026-09-02 04:00:00 -0400" endDate="2026-09-02 05:00:00 -0400"/>
        """);

        Assert.Equal(360, Value(r, MetricKeys.TotalSleepMinutes));
    }

    [Fact]
    public void DroppedDaysAreCountedPerMetric()
    {
        // Silently discarding data is the failure this whole importer is built against.
        var r = Scan("""
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-01 08:00:00 -0400" endDate="2026-09-01 09:00:00 -0400" value="1"/>
        <Record type="HKQuantityTypeIdentifierStepCount" sourceName="Kanth" unit="count" startDate="2026-09-02 08:00:00 -0400" endDate="2026-09-02 09:00:00 -0400" value="2"/>
        """);

        Assert.Contains(r.Warnings, w => w.Contains("2 day(s) of steps"));
    }
}
