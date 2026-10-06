using Maaya.Time;

namespace Maaya.Time.Tests;

// The clock every module now agrees on.
//
// These pin the specific failures that produced wrong days in production, not a general
// notion of correctness: the evening that rolls over in UTC, the two nights a year the
// clocks change, and a configured zone that must beat the machine's own.
public class MaayaClockTests
{
    // Tests share a process-wide zone, so each one sets what it needs and the class
    // restores the default afterwards. xunit runs a class's tests serially.
    public MaayaClockTests() => MaayaClock.Configure("America/New_York");

    private static DateTime Utc(int y, int m, int d, int h, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    // ── The bug this exists for ─────────────────────────────────────────────────

    [Fact]
    public void NineInTheEveningInNewYorkIsStillTodayNotTomorrow()
    {
        // 9pm EDT on 3 October is 01:00 UTC on 4 October. DateOnly.FromDateTime on the
        // UTC value says the 4th, which is a day that has not started where this person
        // lives. This is the whole reason the class exists.
        var instant = Utc(2026, 10, 4, 1);

        Assert.Equal(new DateOnly(2026, 10, 4), DateOnly.FromDateTime(instant));   // the bug
        Assert.Equal(new DateOnly(2026, 10, 3), MaayaClock.DayOf(instant));        // the fix
    }

    [Fact]
    public void JustAfterLocalMidnightIsTheNewDay()
    {
        // 00:30 EDT on the 4th is 04:30 UTC the same calendar date. Both methods agree,
        // which is the other half of the boundary and worth pinning so a "fix" that just
        // subtracts a fixed offset does not slip through.
        Assert.Equal(new DateOnly(2026, 10, 4), MaayaClock.DayOf(Utc(2026, 10, 4, 4, 30)));
        Assert.Equal(new DateOnly(2026, 10, 3), MaayaClock.DayOf(Utc(2026, 10, 4, 3, 59)));
    }

    [Fact]
    public void WinterAndSummerHaveDifferentOffsets()
    {
        // EST is UTC-5 and EDT is UTC-4, so a fixed offset is wrong for half the year.
        // 23:30 local in January is 04:30 UTC the next day; in July it is 03:30.
        Assert.Equal(new DateOnly(2026, 1, 15), MaayaClock.DayOf(Utc(2026, 1, 16, 4, 30)));
        Assert.Equal(new DateOnly(2026, 7, 15), MaayaClock.DayOf(Utc(2026, 7, 16, 3, 30)));
        Assert.Equal(new DateOnly(2026, 7, 16), MaayaClock.DayOf(Utc(2026, 7, 16, 4, 30)));
    }

    // ── Local day boundaries as instants ────────────────────────────────────────

    [Fact]
    public void ALocalDayStartsAtLocalMidnightNotUtcMidnight()
    {
        // Querying "today" with UTC midnight misattributes four or five hours.
        Assert.Equal(Utc(2026, 10, 3, 4), MaayaClock.StartOfDayUtc(new DateOnly(2026, 10, 3)));   // EDT
        Assert.Equal(Utc(2026, 1, 15, 5), MaayaClock.StartOfDayUtc(new DateOnly(2026, 1, 15)));   // EST
    }

    [Fact]
    public void TheEndOfADayIsTheStartOfTheNextSoNothingFallsBetween()
    {
        var day = new DateOnly(2026, 10, 3);
        Assert.Equal(MaayaClock.StartOfDayUtc(day.AddDays(1)), MaayaClock.EndOfDayUtc(day));
    }

    [Fact]
    public void TheDayTheClocksGoBackIsTwentyFiveHours()
    {
        // 1 November 2026. A 23:59:59 upper bound or a fixed 24 hours loses or double
        // counts an hour; an exclusive start-of-next-day bound does not.
        var day = new DateOnly(2026, 11, 1);
        Assert.Equal(25, (MaayaClock.EndOfDayUtc(day) - MaayaClock.StartOfDayUtc(day)).TotalHours);
    }

    [Fact]
    public void TheDayTheClocksGoForwardIsTwentyThreeHours()
    {
        // 8 March 2026.
        var day = new DateOnly(2026, 3, 8);
        Assert.Equal(23, (MaayaClock.EndOfDayUtc(day) - MaayaClock.StartOfDayUtc(day)).TotalHours);
    }

    // ── Wall clock back to an instant ───────────────────────────────────────────

    [Fact]
    public void AWallClockTimeConvertsToTheRightInstant()
    {
        Assert.Equal(Utc(2026, 10, 3, 12), MaayaClock.ToUtc(new DateTime(2026, 10, 3, 8, 0, 0)));   // EDT
        Assert.Equal(Utc(2026, 1, 15, 13), MaayaClock.ToUtc(new DateTime(2026, 1, 15, 8, 0, 0)));   // EST
    }

    [Fact]
    public void ATimeThatDoesNotExistIsResolvedRatherThanThrownOn()
    {
        // 2:30am on 8 March 2026 never happens: the clocks jump from 2:00 to 3:00. A
        // reminder set for it should still fire that night, not throw.
        var instant = MaayaClock.ToUtc(new DateTime(2026, 3, 8, 2, 30, 0));

        Assert.Equal(Utc(2026, 3, 8, 7, 30), instant);   // 3:30 EDT
    }

    [Fact]
    public void ATimeThatHappensTwiceIsResolvedRatherThanThrownOn()
    {
        // 1:30am on 1 November 2026 happens twice. Either reading is defensible; what
        // matters is that it returns one of them.
        var instant = MaayaClock.ToUtc(new DateTime(2026, 11, 1, 1, 30, 0));

        Assert.Contains(instant, new[] { Utc(2026, 11, 1, 5, 30), Utc(2026, 11, 1, 6, 30) });
    }

    [Fact]
    public void RoundTripsThroughUtcAndBack()
    {
        var local = new DateTime(2026, 10, 3, 21, 15, 0);

        Assert.Equal(local, MaayaClock.FromUtc(MaayaClock.ToUtc(local)));
    }

    // ── Configuration ───────────────────────────────────────────────────────────

    [Fact]
    public void ADifferentConfiguredZoneChangesTheDay()
    {
        // 9pm EDT is 6:30am the next morning in Kolkata. Configure() is how a setting
        // changed at runtime reaches every caller, and it has to actually move the day.
        var instant = Utc(2026, 10, 4, 1);

        MaayaClock.Configure("Asia/Kolkata");
        Assert.Equal(new DateOnly(2026, 10, 4), MaayaClock.DayOf(instant));

        MaayaClock.Configure("America/New_York");
        Assert.Equal(new DateOnly(2026, 10, 3), MaayaClock.DayOf(instant));
    }

    [Fact]
    public void AnUnknownZoneIsIgnoredRatherThanTakingTheModuleDown()
    {
        // A typo typed into a settings screen must not change anything.
        MaayaClock.Configure("Not/AZone");

        Assert.Equal("America/New_York", MaayaClock.ZoneId);
    }

    [Fact]
    public void ClearingTheOverrideReturnsToTheDefault()
    {
        MaayaClock.Configure("Asia/Kolkata");
        MaayaClock.Configure(null);

        Assert.NotEqual("Asia/Kolkata", MaayaClock.ZoneId);
    }

    [Fact]
    public void TheMachinesOwnZoneIsNeverTheAnswerWhenAnotherIsConfigured()
    {
        // The reason TimeZoneInfo.Local is not in the resolution order. A developer's
        // laptop is not in the zone the system is configured for, and letting it win
        // makes the same code compute different days on the dev machine and on Everest.
        Assert.Equal("America/New_York", MaayaClock.Zone.Id.Replace("Eastern Standard Time", "America/New_York"));
    }

    // ── Parsing ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-10-03", 2026, 10, 3)]
    [InlineData(" 2026-01-05 ", 2026, 1, 5)]
    public void ADayOnTheWireIsParsedWithoutAnyZone(string raw, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), MaayaClock.ParseDay(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("10/03/2026")]
    [InlineData("2026-10-03T21:00:00Z")]
    public void AnythingElseIsNullSoACallerCanFallBackWithoutATryCatch(string? raw) =>
        Assert.Null(MaayaClock.ParseDay(raw));

    // ── The clock itself ────────────────────────────────────────────────────────

    [Fact]
    public void TodayAgreesWithNowAndYesterdayIsTheDayBefore()
    {
        Assert.Equal(DateOnly.FromDateTime(MaayaClock.Now), MaayaClock.Today);
        Assert.Equal(MaayaClock.Today.AddDays(-1), MaayaClock.Yesterday);
    }

    [Fact]
    public void NowIsALocalReadingNotAnInstant()
    {
        // Unspecified, so nobody can pass it to a UTC API and be silently wrong.
        Assert.Equal(DateTimeKind.Unspecified, MaayaClock.Now.Kind);
    }
}
