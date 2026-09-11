using System.Text;
using Vitara.Application;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// Reading an Apple Health export out of a spreadsheet.
//
// Apple's own export is XML in a zip, so what people actually upload came out of a
// third-party export app -- and those disagree about column names, date formats, units
// and whether a row is a day or a single sample, with nothing in the file declaring
// which. The importer guesses, so the guessing is what these tests pin down.
//
// The failure that matters is not a rejected file. That is visible and fixable. It is a
// file that imports cleanly with weight in pounds recorded as kilos, or sleep in hours
// recorded as minutes: plausible numbers, silently wrong, and every baseline built on
// them wrong too.
public class HealthImportTests
{
    private static ImportPreview Parse(string csv, string name = "export.csv") =>
        HealthImport.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), name);

    // ── Wide format: one row per day ────────────────────────────────────────────

    [Fact]
    public void ReadsAWideExport()
    {
        var p = Parse("""
        Date,Steps,Resting Heart Rate,Heart Rate Variability
        2026-09-01,8431,54,42.5
        2026-09-02,10233,53,45.1
        """);

        Assert.Equal("wide", p.Shape);
        Assert.Equal(new DateOnly(2026, 9, 1), p.FirstDay);
        Assert.Equal(new DateOnly(2026, 9, 2), p.LastDay);
        Assert.Equal(6, p.Readings.Count);
        Assert.Contains(p.Readings, r => r.Metric == MetricKeys.Steps && r.Value == 8431);
        Assert.Contains(p.Readings, r => r.Metric == MetricKeys.HrvRmssd && r.Value == 45.1);
    }

    [Theory]
    [InlineData("Resting Heart Rate")]
    [InlineData("resting_heart_rate")]
    [InlineData("RestingHeartRate")]
    [InlineData("HKQuantityTypeIdentifierRestingHeartRate")]
    [InlineData("Resting Heart Rate (bpm)")]
    public void HeaderSpellingDoesNotMatter(string header)
    {
        // Five exporters, five spellings of the same thing. Normalising instead of
        // listing synonyms is what keeps this from being a maintenance list.
        var p = Parse($"Date,{header}\n2026-09-01,54");
        Assert.Contains(p.Readings, r => r.Metric == MetricKeys.RestingHeartRate);
    }

    [Fact]
    public void HrvIsNotSwallowedByHeartRate()
    {
        // "heartratevariability" starts with "heartrate". Matching the shortest alias
        // first would file HRV as a pulse reading, and the number looks fine either way.
        var p = Parse("Date,Heart Rate Variability\n2026-09-01,42.5");

        var r = Assert.Single(p.Readings);
        Assert.Equal(MetricKeys.HrvRmssd, r.Metric);
    }

    [Fact]
    public void UnknownColumnsAreListedNotDropped()
    {
        // A sheet with columns this system does not track is the normal case, and the
        // user is the only one who can tell whether the skipped one mattered.
        var p = Parse("Date,Steps,Mindful Minutes,Stand Hours\n2026-09-01,8431,10,9");

        Assert.Contains("Mindful Minutes", p.Ignored);
        Assert.Contains("Stand Hours", p.Ignored);
        Assert.Single(p.Readings);
    }

    [Fact]
    public void ARecognisedColumnWithNoValuesIsCalledOut()
    {
        // The most useful thing a preview can say. A column that matched a metric but
        // yielded nothing looks identical to success in a row count.
        var p = Parse("Date,Steps,Resting Heart Rate\n2026-09-01,8431,\n2026-09-02,9000,");

        Assert.Contains(p.Warnings, w => w.Contains("Resting Heart Rate") && w.Contains("no readable values"));
    }

    // ── Long format: one row per sample ─────────────────────────────────────────

    [Fact]
    public void ReadsALongExport()
    {
        var p = Parse("""
        type,startDate,value,unit
        HKQuantityTypeIdentifierStepCount,2026-09-01 08:00:00,4000,count
        HKQuantityTypeIdentifierStepCount,2026-09-01 18:00:00,4431,count
        HKQuantityTypeIdentifierRestingHeartRate,2026-09-01 07:00:00,54,count/min
        """);

        Assert.Equal("long", p.Shape);

        // Steps accumulate over a day, so two samples sum. Averaging them would throw
        // away most of the day's walking.
        var steps = Assert.Single(p.Readings, r => r.Metric == MetricKeys.Steps);
        Assert.Equal(8431, steps.Value);
    }

    [Fact]
    public void RatesAreAveragedNotSummed()
    {
        // Totalling a day of heart-rate readings produces a number in the thousands.
        var p = Parse("""
        type,startDate,value
        RestingHeartRate,2026-09-01,52
        RestingHeartRate,2026-09-01,56
        """);

        var hr = Assert.Single(p.Readings);
        Assert.Equal(54, hr.Value);
    }

    [Fact]
    public void UnknownSampleTypesAreReportedOnce()
    {
        var p = Parse("""
        type,startDate,value
        HKQuantityTypeIdentifierDietaryWater,2026-09-01,500
        HKQuantityTypeIdentifierDietaryWater,2026-09-02,600
        """);

        Assert.Single(p.Ignored);
        Assert.Empty(p.Readings);
    }

    // ── Units: the silent-corruption class ──────────────────────────────────────

    [Fact]
    public void PoundsBecomeKilograms()
    {
        // Weight in pounds stored as kilos reads as a plausible number forever.
        var p = Parse("Date,Body Mass (lb)\n2026-09-01,176.4");

        var r = Assert.Single(p.Readings);
        Assert.Equal(80.0, r.Value, 1);
    }

    [Fact]
    public void KilogramsAreLeftAlone()
    {
        var p = Parse("Date,Body Mass (kg)\n2026-09-01,80");
        Assert.Equal(80.0, Assert.Single(p.Readings).Value, 1);
    }

    [Fact]
    public void SleepInHoursBecomesMinutes()
    {
        // The store is minutes. 7.4 recorded as 7.4 minutes would read as a catastrophic
        // night and light up every detector at once.
        var p = Parse("Date,Sleep Analysis [Asleep]\n2026-09-01,7.4");

        Assert.Equal(444, Assert.Single(p.Readings).Value, 0);
    }

    [Fact]
    public void SleepAlreadyInMinutesIsNotMultiplied()
    {
        var p = Parse("Date,Total Sleep (min)\n2026-09-01,444");
        Assert.Equal(444, Assert.Single(p.Readings).Value, 0);
    }

    [Fact]
    public void SleepWrittenAsAClockTimeIsUnderstood()
    {
        // "7:24" from the exporters that format duration as hh:mm.
        var p = Parse("Date,Sleep Analysis\n2026-09-01,7:24");
        Assert.Equal(444, Assert.Single(p.Readings).Value, 0);
    }

    // ── Dates ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-01")]
    [InlineData("2026/09/01")]
    [InlineData("2026-09-01 23:30:00")]
    [InlineData("1 Sep 2026")]
    public void DateFormatsThatExportersActuallyUse(string date)
    {
        var p = Parse($"Date,Steps\n{date},8431");
        Assert.Equal(new DateOnly(2026, 9, 1), Assert.Single(p.Readings).Day);
    }

    [Fact]
    public void ALateEveningReadingStaysOnItsOwnDay()
    {
        // Normalising a zoned timestamp to UTC files an 11:30pm reading under tomorrow.
        var p = Parse("type,startDate,value\nStepCount,2026-09-01 23:30:00 -04:00,8431");
        Assert.Equal(new DateOnly(2026, 9, 1), Assert.Single(p.Readings).Day);
    }

    [Fact]
    public void UnreadableDatesAreCountedNotGuessed()
    {
        var p = Parse("Date,Steps\n2026-09-01,8431\nnot a date,9000");

        Assert.Single(p.Readings);
        Assert.Contains(p.Warnings, w => w.Contains("1 row(s) skipped"));
    }

    // ── Absent versus zero ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("N/A")]
    [InlineData("NaN")]
    public void EmptyMarkersAreAbsentNotZero(string cell)
    {
        // A missing HRV is not an HRV of zero, and a baseline that averages them in is
        // describing someone else.
        var p = Parse($"Date,Heart Rate Variability\n2026-09-01,{cell}");
        Assert.Empty(p.Readings);
    }

    // ── File shapes ─────────────────────────────────────────────────────────────

    [Fact]
    public void SemicolonFilesAreDetected()
    {
        // A European export parsed as commas arrives as one enormous column.
        var p = Parse("Date;Steps\n2026-09-01;8431");
        Assert.Single(p.Readings);
    }

    [Fact]
    public void QuotedFieldsWithCommasSurvive()
    {
        var rows = HealthImport.ReadDelimited(new MemoryStream(Encoding.UTF8.GetBytes(
            "Date,Note,Steps\n2026-09-01,\"walked, then ran\",8431")));

        Assert.Equal(3, rows[1].Count);
        Assert.Equal("walked, then ran", rows[1][1]);
    }

    [Fact]
    public void DoubledQuotesBecomeOne()
    {
        var rows = HealthImport.ReadDelimited(new MemoryStream(Encoding.UTF8.GetBytes(
            "a,b\n1,\"say \"\"hi\"\"\"")));

        Assert.Equal("say \"hi\"", rows[1][1]);
    }

    [Fact]
    public void ThousandsSeparatorsAreStripped()
    {
        var p = Parse("Date,Steps\n2026-09-01,\"10,233\"");
        Assert.Equal(10233, Assert.Single(p.Readings).Value);
    }

    // ── Files that are not usable ───────────────────────────────────────────────

    [Fact]
    public void NoDateColumnIsRefusedWithTheHeaderItSaw()
    {
        // Refusing is fine; refusing without showing what it read is not — the user
        // cannot fix a file when the error does not say what was wrong with it.
        var p = Parse("Steps,Calories\n8431,320");

        Assert.Equal("unrecognised", p.Shape);
        Assert.Empty(p.Readings);
        Assert.Contains(p.Warnings, w => w.Contains("Steps"));
    }

    [Fact]
    public void AnEmptyFileSaysSo()
        => Assert.Equal("empty", Parse("").Shape);

    [Fact]
    public void NothingRecognisedIsStatedPlainly()
    {
        var p = Parse("Date,Mindful Minutes\n2026-09-01,10");
        Assert.Contains(p.Warnings, w => w.Contains("no column matched"));
    }
}
