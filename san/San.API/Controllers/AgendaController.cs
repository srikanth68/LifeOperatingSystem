using Microsoft.AspNetCore.Mvc;
using San.Application;
using San.Application.Interfaces;

namespace San.API.Controllers;

// "What am I supposed to be doing?"
//
// Every part of this existed and none of it talked: calendar events, reminders and
// alerts in San, action items in NorthStar, tasks in Aasthi, habits in Karma. Asking
// any one of them is a question about a module. This is the only question the user
// actually asks, and until now nothing could answer it.
//
// Everything is resolved in the user's local timezone, because "today", "overdue" and
// "tomorrow" all change at local midnight and every one of them is a lie computed in
// the container's clock.
[ApiController, Route("api/agenda")]
public class AgendaController(ISanRepository repo, IModuleContextService moduleContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 12, CancellationToken ct = default)
    {
        var tz = await moduleContext.ResolveTimeZoneAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var today = DateOnly.FromDateTime(nowLocal);

        var items = new List<AgendaItem?>();

        // San's own stores — the calendar is San's, not a mirror of anyone else's.
        // Window is deliberately wider than the agenda reports: an event that started
        // yesterday and runs into today is still in progress, and one starting late
        // tomorrow still belongs under "tomorrow".
        foreach (var e in await repo.GetCalendarEventsAsync(nowUtc.AddDays(-1), nowUtc.AddDays(2)))
            items.Add(Agenda.FromEvent(
                e.Title,
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.StartTime, DateTimeKind.Utc), tz),
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.EndTime, DateTimeKind.Utc), tz),
                e.AllDay, e.Location, nowLocal));

        foreach (var r in await repo.GetRemindersAsync())
            items.Add(Agenda.FromReminder(
                r.Text,
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.DueAt, DateTimeKind.Utc), tz),
                r.Done, nowLocal));

        foreach (var a in await repo.GetActiveAlertsAsync())
            items.Add(Agenda.FromAlert(a.Title, string.IsNullOrWhiteSpace(a.Description) ? null : a.Description));

        // Sibling modules. Unreachable ones contribute nothing rather than failing the
        // whole answer — a partial agenda beats an error page.
        foreach (var c in await moduleContext.GetOpenCommitmentsAsync(ct))
            items.Add(Agenda.FromCommitment(c, today, nowLocal));

        foreach (var (name, done) in await moduleContext.GetTodaysHabitsAsync(ct))
            items.Add(Agenda.FromHabit(name, done, nowLocal));

        var ranked = Agenda.Rank(items, Math.Clamp(limit, 1, 50));

        return Ok(new
        {
            asOfLocal = nowLocal,
            partOfDay = TimeAwareness.PartLabel(TimeAwareness.PartOfDay(nowLocal)),
            isWeekend = TimeAwareness.IsWeekend(nowLocal),
            count = ranked.Count,
            // Grouped as well as ranked: the flat order is what San should read out,
            // the groups are what a UI wants.
            buckets = ranked.GroupBy(i => i.Bucket)
                            .ToDictionary(g => g.Key, g => g.Count()),
            items = ranked.Select(i => new
            {
                i.Kind, i.Title, i.Bucket, i.Source, i.Detail,
                at = i.WhenLocal?.ToString("h:mm tt"),
                atIso = i.WhenLocal,
            }),
        });
    }
}
