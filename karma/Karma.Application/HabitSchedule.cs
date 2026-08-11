using Karma.Domain.Entities;

namespace Karma.Application;

// Decides whether a habit's reminder is due right now.
//
// This was an exact string equality against the current minute — `NotifyTime !=
// "HH:mm"` skipped the habit — combined with a once-per-day guard. That only ever
// fires if the process happens to be alive during that precise minute, and if it
// isn't, the reminder is not delayed, it is LOST for the day. The realistic trigger
// is a deploy: every `docker compose up -d` restarts Karma, and a restart spanning
// 07:00 silently eats a 07:00 reminder. Tick drift under load and the spring-forward
// DST gap (a 02:30 reminder on that date has no 02:30 to match) do the same.
//
// So the question is "is this due and unsent" rather than "is it exactly now", which
// makes a missed minute self-heal on the next tick.
public static class HabitSchedule
{
    // How late a reminder may still be delivered. Long enough to ride out a deploy,
    // a container restart, or a stalled tick; short enough that a habit whose time
    // passed hours ago stays missed instead of arriving at bedtime, which would be
    // worse than useless — a 07:00 nudge at 22:00 teaches you to ignore the channel.
    public static readonly TimeSpan GracePeriod = TimeSpan.FromMinutes(45);

    public static bool IsDue(Habit habit, DateTime now)
    {
        if (!habit.IsActive) return false;
        if (!TryParseNotifyTime(habit.NotifyTime, out var notifyAt)) return false;

        var today = DateOnly.FromDateTime(now);
        if (habit.LastNotificationSentOn == today) return false;
        if (!habit.NotifyDays.Contains((int)now.DayOfWeek)) return false;

        var scheduled = now.Date.Add(notifyAt);
        if (now < scheduled) return false;               // not yet
        return now - scheduled <= GracePeriod;           // due, and not yet stale
    }

    // Tolerates "7:00" as well as "07:00", and rejects anything else rather than
    // throwing — a malformed value on one habit must not stop every other habit's
    // reminder for the rest of the day.
    public static bool TryParseNotifyTime(string? raw, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var parts = raw.Split(':');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m)) return false;
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;

        time = new TimeSpan(h, m, 0);
        return true;
    }
}
