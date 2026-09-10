using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using San.Application;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

[ApiController, Route("api/calendar")]
public class CalendarController(ISanRepository repo, IGoogleCalendarService google, IModuleContextService moduleContext) : ControllerBase
{
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var start = from ?? DateTime.UtcNow.Date;
        var end = to ?? start.AddDays(7);
        var events = await repo.GetCalendarEventsAsync(start, end);
        var tzAll = await moduleContext.ResolveTimeZoneAsync();
        return Ok(events.Select(e => ToResult(e, tzAll)));
    }

    [HttpGet("now-next")]
    public async Task<IActionResult> NowNext([FromQuery] int hours = 3)
    {
        var now = DateTime.UtcNow;
        var windowEnd = now.AddHours(hours);
        var events = await repo.GetCalendarEventsAsync(now.AddDays(-1), windowEnd);

        var current = events.FirstOrDefault(e => e.StartTime <= now && e.EndTime >= now);
        var upcoming = events.Where(e => e.StartTime > now && e.StartTime <= windowEnd).ToList();
        var tzNow = await moduleContext.ResolveTimeZoneAsync();

        return Ok(new NowNextResult(
            current is not null ? ToResult(current, tzNow) : null,
            upcoming.Select(e => ToResult(e, tzNow)).ToList(),
            now));
    }

    [HttpPost("events")]
    public async Task<IActionResult> Create([FromBody] CalendarEventUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");

        var ev = new CalendarEvent
        {
            Title = req.Title,
            Description = req.Description,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Location = req.Location,
            AllDay = req.AllDay,
            Source = "manual"
        };

        var saved = await repo.UpsertCalendarEventAsync(ev);
        return Ok(ToResult(saved, await moduleContext.ResolveTimeZoneAsync()));
    }

    [AllowAnonymous]
    [HttpGet("auth")]
    public IActionResult Auth()
    {
        var url = google.GetAuthUrl();
        if (string.IsNullOrEmpty(url))
            return BadRequest(new { error = "Google Calendar not configured. Set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET." });
        return Ok(new { url });
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return BadRequest("Missing code parameter.");
        var ok = await google.HandleCallbackAsync(code);
        return ok ? Ok(new { message = "Google Calendar authorized successfully." })
                  : StatusCode(500, new { error = "Failed to exchange code for tokens." });
    }

    [HttpGet("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        if (!google.IsConfiguredAndAuthorized)
            return BadRequest(new { error = "Google Calendar not configured or not authorized. Call /api/calendar/auth first." });
        var count = await google.SyncEventsAsync(ct);
        return Ok(new { synced = count });
    }

    private static CalendarEventResult ToResult(CalendarEvent e, TimeZoneInfo tz) =>
        new(e.Id, e.Title, e.Description,
            LocalTimeText.AsUtc(e.StartTime), LocalTimeText.AsUtc(e.EndTime),
            e.Location, e.Source, e.ExternalId, e.CalendarName,
            e.AllDay, LocalTimeText.AsUtc(e.CreatedAt), LocalTimeText.AsUtc(e.UpdatedAt),
            LocalTimeText.Local(e.StartTime, tz), LocalTimeText.Local(e.EndTime, tz));
}
