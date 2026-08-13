using San.Application;

namespace San.Tests;

// Uses the default 22:00-07:00 window throughout: QUIET_HOURS_START/END are read from
// process-wide environment on every call, so a test that set them would leak into
// whatever ran alongside it.
public class QuietHoursTests
{
    private static DateTime At(int hour, int minute = 0) => new(2026, 8, 12, hour, minute, 0, DateTimeKind.Unspecified);

    // The window wraps midnight, which is the case a naive `hour >= start && hour < end`
    // gets exactly backwards — it would make the quiet period the whole working day.
    [Theory]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]    // window ends AT 7
    [InlineData(12, false)]
    [InlineData(21, false)]   // and starts AT 22
    public void QuietWindowWrapsMidnight(int hour, bool expected)
        => Assert.Equal(expected, QuietHours.IsQuiet(At(hour)));

    // The whole point of a critical severity is that it's worth waking someone for.
    [Fact]
    public void CriticalIsNeverHeld()
    {
        Assert.False(QuietHours.ShouldHold("critical", At(3)));
        Assert.False(QuietHours.ShouldHold("CRITICAL", At(3)));
    }

    [Theory]
    [InlineData("info")]
    [InlineData("warning")]
    [InlineData("")]
    public void EverythingElseIsHeldOvernight(string severity)
        => Assert.True(QuietHours.ShouldHold(severity, At(3)));

    [Fact]
    public void NothingIsHeldDuringTheDay()
    {
        Assert.False(QuietHours.ShouldHold("info", At(14)));
        Assert.False(QuietHours.ShouldHold("warning", At(9)));
    }

    // Both sides of midnight have to resolve to the SAME morning, and it must be the
    // next one — a held message that waits an extra 24 hours is worse than one sent late.
    [Fact]
    public void LateNightMessageWaitsForThisMorning()
        => Assert.Equal(new DateTime(2026, 8, 12, 7, 0, 0), QuietHours.NextOpening(At(2)));

    [Fact]
    public void BeforeMidnightMessageWaitsForTomorrowMorning()
        => Assert.Equal(new DateTime(2026, 8, 13, 7, 0, 0), QuietHours.NextOpening(At(23)));

    [Fact]
    public void OutsideQuietHoursThereIsNothingToWaitFor()
        => Assert.Equal(At(14), QuietHours.NextOpening(At(14)));
}
