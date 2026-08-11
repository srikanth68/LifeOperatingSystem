using Karma.Application;
using Karma.Domain.Entities;

namespace Karma.Tests;

// The old rule was exact-minute equality, so every one of the "recovers" cases below
// silently dropped the reminder for the whole day. The "does not fire" cases are the
// other half of the bargain: making delivery recoverable must not make it noisy.
public class HabitScheduleTests
{
    // Saturday 2026-08-15. DayOfWeek 6.
    private static DateTime At(int hour, int minute) => new(2026, 8, 15, hour, minute, 0, DateTimeKind.Unspecified);

    private static Habit Habit(string? notifyTime = "07:00", DateOnly? lastSent = null, bool active = true)
        => new()
        {
            Name = "Morning walk",
            NotifyTime = notifyTime,
            IsActive = active,
            LastNotificationSentOn = lastSent,
            NotifyDays = [0, 1, 2, 3, 4, 5, 6],
        };

    [Fact]
    public void FiresOnTheExactMinute()
        => Assert.True(HabitSchedule.IsDue(Habit(), At(7, 0)));

    [Fact]
    public void DoesNotFireBeforeItsTime()
        => Assert.False(HabitSchedule.IsDue(Habit(), At(6, 59)));

    // THE BUG. A deploy spanning 07:00 restarts Karma; the first tick afterwards used
    // to see 07:03 != 07:00 and the reminder was gone until tomorrow.
    [Fact]
    public void RecoversAReminderMissedByARestart()
        => Assert.True(HabitSchedule.IsDue(Habit(), At(7, 3)));

    [Fact]
    public void RecoversRightUpToTheEdgeOfTheGracePeriod()
        => Assert.True(HabitSchedule.IsDue(Habit(), At(7, 0).Add(HabitSchedule.GracePeriod)));

    // A 07:00 nudge arriving at 22:00 is worse than a missed one — it teaches you to
    // ignore the channel.
    [Fact]
    public void StopsTryingOnceItIsStale()
        => Assert.False(HabitSchedule.IsDue(Habit(), At(7, 0).Add(HabitSchedule.GracePeriod).AddMinutes(1)));

    [Fact]
    public void NeverSendsTwiceInADay()
        => Assert.False(HabitSchedule.IsDue(
            Habit(lastSent: new DateOnly(2026, 8, 15)), At(7, 10)));

    [Fact]
    public void YesterdaysSendDoesNotBlockToday()
        => Assert.True(HabitSchedule.IsDue(
            Habit(lastSent: new DateOnly(2026, 8, 14)), At(7, 0)));

    [Fact]
    public void RespectsTheDaysOfWeekItIsScheduledFor()
    {
        var weekdaysOnly = Habit();
        weekdaysOnly.NotifyDays = [1, 2, 3, 4, 5];   // Mon–Fri; the 15th is a Saturday
        Assert.False(HabitSchedule.IsDue(weekdaysOnly, At(7, 0)));
    }

    [Fact]
    public void InactiveHabitsStaySilent()
        => Assert.False(HabitSchedule.IsDue(Habit(active: false), At(7, 0)));

    [Fact]
    public void NoNotifyTimeMeansNotificationsAreOff()
        => Assert.False(HabitSchedule.IsDue(Habit(notifyTime: null), At(7, 0)));

    // One habit with a bad value must not throw and take every other habit's reminder
    // down with it for the rest of the day.
    [Theory]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("nonsense")]
    [InlineData("25:00")]
    [InlineData("07:61")]
    [InlineData("07:xx")]
    public void MalformedTimesAreIgnoredRatherThanThrowing(string raw)
        => Assert.False(HabitSchedule.IsDue(Habit(notifyTime: raw), At(7, 0)));

    [Theory]
    [InlineData("7:00", 7, 0)]
    [InlineData("07:00", 7, 0)]
    [InlineData("23:59", 23, 59)]
    [InlineData("00:00", 0, 0)]
    public void ParsesBothPaddedAndUnpaddedTimes(string raw, int h, int m)
    {
        Assert.True(HabitSchedule.TryParseNotifyTime(raw, out var t));
        Assert.Equal(new TimeSpan(h, m, 0), t);
    }

    // Spring forward: 2026-03-08, local 02:00 jumps to 03:00, so a 02:30 habit has no
    // 02:30 to match. Exact equality dropped it; the grace window delivers it at 03:00.
    [Fact]
    public void DeliversAcrossTheSpringForwardGap()
    {
        var h = Habit(notifyTime: "02:30");
        var justAfterTheGap = new DateTime(2026, 3, 8, 3, 0, 0, DateTimeKind.Unspecified);
        Assert.True(HabitSchedule.IsDue(h, justAfterTheGap));
    }

    [Fact]
    public void MidnightHabitFiresAtMidnight()
        => Assert.True(HabitSchedule.IsDue(Habit(notifyTime: "00:00"), At(0, 0)));

    // Guards the grace window against being widened to something that would let a
    // late-evening reminder leak into the next day, where the LastNotificationSentOn
    // date check is keyed to a day that has already rolled over.
    [Fact]
    public void GracePeriodCannotSpanMidnight()
    {
        Assert.True(HabitSchedule.GracePeriod < TimeSpan.FromHours(1));
        Assert.False(HabitSchedule.IsDue(Habit(notifyTime: "23:59"), At(23, 59).AddMinutes(5).AddDays(0)));
    }
}
