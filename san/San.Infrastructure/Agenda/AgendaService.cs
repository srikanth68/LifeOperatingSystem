using San.Application;
using San.Application.Interfaces;

namespace San.Infrastructure.Agenda;

// Assembles the ranked agenda from every store that holds something the user is on
// the hook for.
//
// Behind an interface rather than living in the controller because San.API and
// San.Worker are separate containers: the morning brief needs exactly this list and
// must not have to ask the API over HTTP for it. Same reasoning as IHealthProbe.
public class AgendaService(ISanRepository repo, IModuleContextService moduleContext) : IAgendaService
{
    public async Task<List<AgendaItem>> BuildAsync(int limit = 12, CancellationToken ct = default)
    {
        var tz = await moduleContext.ResolveTimeZoneAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var today = DateOnly.FromDateTime(nowLocal);

        var items = new List<AgendaItem?>();

        // Window is deliberately wider than the agenda reports: an event that started
        // yesterday and runs into today is still in progress, and one starting late
        // tomorrow still belongs under "tomorrow".
        foreach (var e in await repo.GetCalendarEventsAsync(nowUtc.AddDays(-1), nowUtc.AddDays(2)))
            items.Add(San.Application.Agenda.FromEvent(
                e.Title,
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.StartTime, DateTimeKind.Utc), tz),
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.EndTime, DateTimeKind.Utc), tz),
                e.AllDay, e.Location, nowLocal));

        foreach (var r in await repo.GetRemindersAsync())
            items.Add(San.Application.Agenda.FromReminder(
                r.Text,
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(r.DueAt, DateTimeKind.Utc), tz),
                r.Done, nowLocal));

        foreach (var a in await repo.GetActiveAlertsAsync())
            items.Add(San.Application.Agenda.FromAlert(
                a.Title, string.IsNullOrWhiteSpace(a.Description) ? null : a.Description));

        // Sibling modules. Unreachable ones contribute nothing rather than failing the
        // whole answer — a partial agenda beats an error page, and beats no brief.
        foreach (var c in await moduleContext.GetOpenCommitmentsAsync(ct))
            items.Add(San.Application.Agenda.FromCommitment(c, today, nowLocal));

        foreach (var (name, done) in await moduleContext.GetTodaysHabitsAsync(ct))
            items.Add(San.Application.Agenda.FromHabit(name, done, nowLocal));

        return San.Application.Agenda.Rank(items, Math.Clamp(limit, 1, 50));
    }
}
