namespace San.Application;

// Turns a timestamp into the things a person actually reasons with.
//
// San already had the clock — the time context has always said "it is 3:42 PM" and
// "your previous message was 4 hours ago". What it lacked was any sense of what that
// MEANS. 15:42 is a fact; "mid-afternoon on a workday, four hours since we last
// spoke" is a situation, and only the second one can change how San behaves.
//
// Computed here rather than left to the model on purpose, and for the same reason as
// everywhere else in this system: gemma-4-E4B is reliable at acting on "it is late
// night" and unreliable at deriving that from a timestamp every single turn. The
// facts are deterministic; what to DO about them is prompt guidance, which is the
// user's to write.
public static class TimeAwareness
{
    public enum Part { LateNight, EarlyMorning, Morning, Midday, Afternoon, Evening, Night }

    // Boundaries chosen for how the day is lived, not for even thirds. Late night runs
    // past midnight because 1 AM belongs to the night before, which is also why a
    // calendar-day comparison is the wrong tool for this.
    public static Part PartOfDay(DateTime local) => local.Hour switch
    {
        >= 0 and < 5 => Part.LateNight,
        >= 5 and < 8 => Part.EarlyMorning,
        >= 8 and < 12 => Part.Morning,
        >= 12 and < 14 => Part.Midday,
        >= 14 and < 18 => Part.Afternoon,
        >= 18 and < 22 => Part.Evening,
        _ => Part.Night,
    };

    public static string PartLabel(Part p) => p switch
    {
        Part.LateNight => "late night",
        Part.EarlyMorning => "early morning",
        Part.Morning => "morning",
        Part.Midday => "midday",
        Part.Afternoon => "afternoon",
        Part.Evening => "evening",
        _ => "night",
    };

    public static bool IsWeekend(DateTime local) =>
        local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    // How long the user has been gone, as a category rather than a duration. The
    // duration alone doesn't say what to do with it — the same four hours means
    // "still the same afternoon" at 3 PM and "you slept" at 6 AM.
    public enum Gap { FirstEver, Continuing, SameSession, LaterToday, Overnight, SeveralDays, Weeks, LongAbsence }

    public static Gap ClassifyGap(DateTime? lastSeenUtc, DateTime nowUtc, TimeZoneInfo tz)
    {
        if (lastSeenUtc is not { } seen) return Gap.FirstEver;

        var elapsed = nowUtc - seen;
        if (elapsed < TimeSpan.Zero) return Gap.Continuing;      // clock skew; treat as now
        if (elapsed < TimeSpan.FromMinutes(5)) return Gap.Continuing;
        if (elapsed < TimeSpan.FromMinutes(90)) return Gap.SameSession;

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var seenLocal = TimeZoneInfo.ConvertTimeFromUtc(seen, tz);

        // Calendar days apart, not hours elapsed: 11 PM to 7 AM is eight hours and a
        // different day, and it is the different day that matters.
        var days = (nowLocal.Date - seenLocal.Date).Days;

        if (days == 0) return Gap.LaterToday;
        if (days == 1) return Gap.Overnight;
        if (days < 7) return Gap.SeveralDays;
        if (days < 30) return Gap.Weeks;
        return Gap.LongAbsence;
    }

    // Written as something San can act on directly. Deliberately states the situation
    // and not the desired behaviour — "don't re-greet them" is guidance the user owns.
    public static string GapLabel(Gap gap) => gap switch
    {
        Gap.FirstEver => "This is the start of the conversation.",
        Gap.Continuing => "This is a continuation of the conversation you are already having.",
        Gap.SameSession => "You are still in the same session, with a short break.",
        Gap.LaterToday => "This is a new exchange later on the same day.",
        Gap.Overnight => "The user has not spoken to you since yesterday.",
        Gap.SeveralDays => "The user has been away for a few days.",
        Gap.Weeks => "The user has been away for weeks — things may have changed since.",
        _ => "The user has been away for a month or more — do not assume anything is still current.",
    };

    // The line handed to the model. One sentence, plain facts, no instructions.
    public static string Describe(DateTime nowLocal, DateTime? lastSeenUtc, DateTime nowUtc, TimeZoneInfo tz)
    {
        var part = PartLabel(PartOfDay(nowLocal));
        var kind = IsWeekend(nowLocal) ? "weekend" : "weekday";
        var gap = ClassifyGap(lastSeenUtc, nowUtc, tz);
        return $"It is {part} on a {kind}. {GapLabel(gap)}";
    }
}

// When San is allowed to make the user's phone buzz.
//
// The workers run on 15-minute timers with no notion of hour, so a "your bill is due"
// push at 3 AM is technically correct and practically hostile — and a channel that
// wakes you for a $25 subscription is a channel you learn to ignore, which costs the
// alerts that actually mattered.
//
// Nothing is dropped: a held finding still reaches NorthStar immediately, and the
// Telegram message is queued until morning. Critical passes straight through, because
// the entire point of a critical severity is that it is worth the interruption.
public static class QuietHours
{
    private static int Hour(string env, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(env), out var h) && h is >= 0 and <= 23 ? h : fallback;

    public static int StartHour => Hour("QUIET_HOURS_START", 22);
    public static int EndHour => Hour("QUIET_HOURS_END", 7);

    public static bool IsQuiet(DateTime local)
    {
        var start = StartHour;
        var end = EndHour;
        if (start == end) return false;                       // configured off
        return start < end
            ? local.Hour >= start && local.Hour < end          // e.g. 01:00–06:00
            : local.Hour >= start || local.Hour < end;         // wraps midnight, the normal case
    }

    public static bool ShouldHold(string severity, DateTime local) =>
        IsQuiet(local) && !string.Equals(severity, "critical", StringComparison.OrdinalIgnoreCase);

    // When the current quiet period ends, in local time — what a held message waits for.
    public static DateTime NextOpening(DateTime local)
    {
        if (!IsQuiet(local)) return local;
        var today = local.Date.AddHours(EndHour);
        return local < today ? today : today.AddDays(1);
    }
}
