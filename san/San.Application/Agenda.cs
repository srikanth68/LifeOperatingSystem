namespace San.Application;

// One thing on the user's plate, from whichever module holds it.
public record AgendaItem(
    string Kind,          // event | reminder | alert | action | task | habit
    string Title,
    DateTime? WhenLocal,  // null = no time, it just needs doing
    string Bucket,        // now | soon | today | tomorrow | overdue | open
    int Rank,             // lower sorts first
    string Source,
    string? Detail);

// Merges everything the user is on the hook for into ONE ordered answer.
//
// The pieces all existed and none of them talked: calendar events, reminders and
// alerts in San, action items in NorthStar, tasks in Aasthi, habits in Karma. Five
// stores, five answers, and no way to ask the only question that matters — "what am
// I supposed to be doing?" Asking a module is a question about a module; this is a
// question about the day.
//
// Ranking is the whole feature. A merged list in arbitrary order is barely better
// than five lists, because the user still has to do the triage themselves — which is
// the work they wanted handed off.
public static class Agenda
{
    // Anything starting within this window is happening rather than scheduled, and
    // belongs at the top regardless of what else is outstanding.
    private static readonly TimeSpan NowWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SoonWindow = TimeSpan.FromHours(3);

    // Rank bands. Gaps left between them so items inside a band can be ordered by
    // time without colliding with the next band.
    private const int RankInProgress = 0;
    private const int RankNow = 100;
    private const int RankOverdue = 200;
    private const int RankSoon = 300;
    private const int RankToday = 400;
    private const int RankTomorrow = 500;
    private const int RankOpen = 600;

    public static AgendaItem? FromEvent(
        string title, DateTime startLocal, DateTime endLocal, bool allDay, string? location, DateTime nowLocal)
    {
        // All-day events have no useful time to sort by, so they sit with the day
        // rather than pretending to start at midnight.
        if (allDay)
        {
            if (startLocal.Date < nowLocal.Date || startLocal.Date > nowLocal.Date.AddDays(1)) return null;
            var isToday = startLocal.Date == nowLocal.Date;
            return new AgendaItem("event", title, null, isToday ? "today" : "tomorrow",
                (isToday ? RankToday : RankTomorrow) + 50, "calendar", location);
        }

        // Already started and not finished — the single most relevant thing there is.
        if (startLocal <= nowLocal && endLocal > nowLocal)
            return new AgendaItem("event", title, startLocal, "now", RankInProgress, "calendar",
                location is null ? "in progress" : $"in progress — {location}");

        if (endLocal <= nowLocal) return null;   // over; not part of what's ahead

        var until = startLocal - nowLocal;
        if (until <= NowWindow)
            return new AgendaItem("event", title, startLocal, "now", RankNow + Minutes(until), "calendar", location);
        if (until <= SoonWindow)
            return new AgendaItem("event", title, startLocal, "soon", RankSoon + Minutes(until), "calendar", location);
        if (startLocal.Date == nowLocal.Date)
            return new AgendaItem("event", title, startLocal, "today", RankToday + Minutes(until), "calendar", location);
        if (startLocal.Date == nowLocal.Date.AddDays(1))
            return new AgendaItem("event", title, startLocal, "tomorrow", RankTomorrow + Minutes(until), "calendar", location);

        return null;   // further out than the agenda is asking about
    }

    public static AgendaItem? FromReminder(string text, DateTime dueLocal, bool done, DateTime nowLocal)
    {
        if (done) return null;

        if (dueLocal < nowLocal)
        {
            // Overdue outranks almost everything: a reminder that fired and was not
            // acted on is the clearest possible signal of something dropped.
            var late = nowLocal - dueLocal;
            return new AgendaItem("reminder", text, dueLocal, "overdue",
                RankOverdue - Math.Min((int)late.TotalHours, 99), "reminders",
                late.TotalHours < 24 ? $"was due {Humanise(late)} ago" : $"was due {dueLocal:MMM d}");
        }

        var until = dueLocal - nowLocal;
        if (until <= NowWindow) return new AgendaItem("reminder", text, dueLocal, "now", RankNow + Minutes(until), "reminders", null);
        if (until <= SoonWindow) return new AgendaItem("reminder", text, dueLocal, "soon", RankSoon + Minutes(until), "reminders", null);
        if (dueLocal.Date == nowLocal.Date) return new AgendaItem("reminder", text, dueLocal, "today", RankToday + Minutes(until), "reminders", null);
        if (dueLocal.Date == nowLocal.Date.AddDays(1)) return new AgendaItem("reminder", text, dueLocal, "tomorrow", RankTomorrow + Minutes(until), "reminders", null);
        return null;
    }

    public static AgendaItem FromAlert(string title, string? description) =>
        // An active alert has already decided it is worth attention; it sits just
        // under things happening right now.
        new("alert", title, null, "now", RankNow + 50, "alerts", description);

    public static AgendaItem? FromCommitment(Commitment c, DateOnly today, DateTime nowLocal)
    {
        var kind = c.Source == "aasthi" ? "task" : "action";

        if (c.DueOn is { } due)
        {
            var daysLate = today.DayNumber - due.DayNumber;
            if (daysLate > 0)
                return new AgendaItem(kind, c.Title, null, "overdue",
                    RankOverdue - Math.Min(daysLate, 99), c.Source,
                    daysLate == 1 ? "due yesterday" : $"due {daysLate} days ago");

            if (due.DayNumber == today.DayNumber)
                return new AgendaItem(kind, c.Title, null, "today", RankToday + 90, c.Source, "due today");
            if (due.DayNumber == today.DayNumber + 1)
                return new AgendaItem(kind, c.Title, null, "tomorrow", RankTomorrow + 90, c.Source, "due tomorrow");

            // Dated but further out — not part of "what's on now".
            return null;
        }

        // No date. Present but at the bottom, ordered by the priority the user gave it.
        return new AgendaItem(kind, c.Title, null, "open", RankOpen + c.Priority, c.Source, c.Context);
    }

    // Only worth mentioning while there is still a realistic chance of doing it —
    // listing an unticked habit at 11pm is a reproach, not an agenda.
    public static AgendaItem? FromHabit(string name, bool doneToday, DateTime nowLocal) =>
        doneToday || nowLocal.Hour >= 21 || nowLocal.Hour < 6
            ? null
            : new AgendaItem("habit", name, null, "today", RankOpen - 50, "karma", "not yet today");

    public static List<AgendaItem> Rank(IEnumerable<AgendaItem?> items, int max = 12) =>
        items.Where(i => i is not null).Select(i => i!)
             .OrderBy(i => i.Rank)
             .ThenBy(i => i.WhenLocal ?? DateTime.MaxValue)
             .Take(max)
             .ToList();

    private static int Minutes(TimeSpan t) => Math.Clamp((int)t.TotalMinutes, 0, 99);

    private static string Humanise(TimeSpan t) =>
        t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m" : $"{(int)t.TotalHours}h";
}
