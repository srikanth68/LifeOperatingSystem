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
// The assembly lives in IAgendaService, not here, because the morning brief pushes
// the same list from San.Worker — a separate container that must not have to ask this
// API for it. One implementation means the pull and the push can never disagree about
// what today looks like.
[ApiController, Route("api/agenda")]
public class AgendaController(IAgendaService agenda, IModuleContextService moduleContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 12, CancellationToken ct = default)
    {
        var items = await agenda.BuildAsync(limit, ct);

        var tz = await moduleContext.ResolveTimeZoneAsync(ct);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

        return Ok(new
        {
            asOfLocal = nowLocal,
            partOfDay = TimeAwareness.PartLabel(TimeAwareness.PartOfDay(nowLocal)),
            isWeekend = TimeAwareness.IsWeekend(nowLocal),
            count = items.Count,
            // Grouped as well as ranked: the flat order is what San should read out,
            // the groups are what a UI wants.
            buckets = items.GroupBy(i => i.Bucket).ToDictionary(g => g.Key, g => g.Count()),
            items = items.Select(i => new
            {
                i.Kind, i.Title, i.Bucket, i.Source, i.Detail,
                at = i.WhenLocal?.ToString("h:mm tt"),
                atIso = i.WhenLocal,
            }),
        });
    }
}
