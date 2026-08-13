using San.Application;

namespace San.Tests;

// These feed the line San is handed every turn, and the workers' decision about whether
// to make a phone buzz. Both run unattended, so a wrong boundary here is the kind of
// bug that goes unnoticed for weeks — it doesn't crash, it just makes San slightly and
// consistently wrong about the situation.
public class TimeAwarenessTests
{
    private static DateTime At(int hour, int minute = 0) => new(2026, 8, 12, hour, minute, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(0, TimeAwareness.Part.LateNight)]
    [InlineData(4, TimeAwareness.Part.LateNight)]
    [InlineData(5, TimeAwareness.Part.EarlyMorning)]
    [InlineData(7, TimeAwareness.Part.EarlyMorning)]
    [InlineData(8, TimeAwareness.Part.Morning)]
    [InlineData(11, TimeAwareness.Part.Morning)]
    [InlineData(12, TimeAwareness.Part.Midday)]
    [InlineData(13, TimeAwareness.Part.Midday)]
    [InlineData(14, TimeAwareness.Part.Afternoon)]
    [InlineData(17, TimeAwareness.Part.Afternoon)]
    [InlineData(18, TimeAwareness.Part.Evening)]
    [InlineData(21, TimeAwareness.Part.Evening)]
    [InlineData(22, TimeAwareness.Part.Night)]
    [InlineData(23, TimeAwareness.Part.Night)]
    public void PartOfDayBoundaries(int hour, TimeAwareness.Part expected)
        => Assert.Equal(expected, TimeAwareness.PartOfDay(At(hour)));

    // The comment claims 1 AM "belongs to the night before". Pinned, because it's the
    // boundary most likely to get 'tidied' into even thirds by someone later.
    [Fact]
    public void OneAmIsLateNightNotEarlyMorning()
        => Assert.Equal(TimeAwareness.Part.LateNight, TimeAwareness.PartOfDay(At(1)));

    [Theory]
    [InlineData(2026, 8, 15, true)]   // Saturday
    [InlineData(2026, 8, 16, true)]   // Sunday
    [InlineData(2026, 8, 17, false)]  // Monday
    [InlineData(2026, 8, 14, false)]  // Friday
    public void WeekendDetection(int y, int m, int d, bool expected)
        => Assert.Equal(expected, TimeAwareness.IsWeekend(new DateTime(y, m, d, 12, 0, 0)));

    // ── Gap classification ────────────────────────────────────────────────────
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static TimeAwareness.Gap Gap(DateTime? seen, DateTime now)
        => TimeAwareness.ClassifyGap(seen, now, Utc);

    [Fact]
    public void NoPriorMessageIsFirstEver()
        => Assert.Equal(TimeAwareness.Gap.FirstEver, Gap(null, new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void WithinFiveMinutesIsContinuing()
    {
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeAwareness.Gap.Continuing, Gap(now.AddMinutes(-4), now));
    }

    // A clock that jumps backwards must not produce a nonsense category — a negative
    // elapsed reads as "right now", not as a month-long absence.
    [Fact]
    public void ClockSkewIsTreatedAsContinuing()
    {
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeAwareness.Gap.Continuing, Gap(now.AddMinutes(30), now));
    }

    [Fact]
    public void UnderNinetyMinutesIsSameSession()
    {
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeAwareness.Gap.SameSession, Gap(now.AddMinutes(-60), now));
    }

    // The behaviour the class exists for: the same elapsed time means different things
    // depending on whether a night passed. Eight hours inside one day is "later today";
    // eight hours across midnight is "since yesterday".
    [Fact]
    public void SameElapsedTimeDiffersByWhetherADayTurned()
    {
        var sameDay = new DateTime(2026, 8, 12, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeAwareness.Gap.LaterToday, Gap(sameDay.AddHours(-8), sameDay));   // 12:00 -> 20:00

        var acrossMidnight = new DateTime(2026, 8, 13, 7, 0, 0, DateTimeKind.Utc);
        Assert.Equal(TimeAwareness.Gap.Overnight, Gap(acrossMidnight.AddHours(-8), acrossMidnight)); // 23:00 -> 07:00
    }

    [Theory]
    [InlineData(3, TimeAwareness.Gap.SeveralDays)]
    [InlineData(6, TimeAwareness.Gap.SeveralDays)]
    [InlineData(7, TimeAwareness.Gap.Weeks)]
    [InlineData(29, TimeAwareness.Gap.Weeks)]
    [InlineData(30, TimeAwareness.Gap.LongAbsence)]
    [InlineData(90, TimeAwareness.Gap.LongAbsence)]
    public void LongerAbsencesEscalate(int daysAgo, TimeAwareness.Gap expected)
    {
        var now = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, Gap(now.AddDays(-daysAgo), now));
    }

    [Fact]
    public void DescribeReadsAsPlainFacts()
    {
        var now = new DateTime(2026, 8, 15, 21, 0, 0, DateTimeKind.Utc);  // Saturday evening
        var text = TimeAwareness.Describe(now, now.AddDays(-2), now, Utc);
        Assert.Contains("evening", text);
        Assert.Contains("weekend", text);
        Assert.Contains("few days", text);
    }
}
