using Karma.Application.DTOs;
using Karma.Application.Interfaces;
using Karma.Domain.Entities;
using Karma.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace Karma.API.Controllers;

[ApiController, Route("api/habits")]
public class HabitsController(IKarmaRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool? active)
    {
        var habits = await repo.GetHabitsAsync(active ?? false);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todayLogs = await repo.GetLogsForDateAsync(today);
        var logMap = todayLogs.ToDictionary(l => l.HabitId, l => l.Completed);

        var results = new List<HabitResult>();
        foreach (var h in habits)
        {
            var allLogs = await repo.GetHabitLogsAsync(h.Id, today.AddDays(-365), today);
            var (cur, best) = KarmaRepository.ComputeStreaks(allLogs, today);
            results.Add(ToResult(h, cur, best, logMap.TryGetValue(h.Id, out var done) ? done : null));
        }
        return Ok(results);
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var habits = await repo.GetHabitsAsync(activeOnly: true);
        var todayLogs = await repo.GetLogsForDateAsync(today);
        var logMap = todayLogs.ToDictionary(l => l.HabitId, l => l.Completed);

        var results = new List<HabitResult>();
        foreach (var h in habits)
        {
            var allLogs = await repo.GetHabitLogsAsync(h.Id, today.AddDays(-365), today);
            var (cur, best) = KarmaRepository.ComputeStreaks(allLogs, today);
            results.Add(ToResult(h, cur, best, logMap.TryGetValue(h.Id, out var done) ? done : null));
        }
        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var h = await repo.GetHabitAsync(id);
        if (h is null) return NotFound();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var allLogs = await repo.GetHabitLogsAsync(h.Id, today.AddDays(-365), today);
        var (cur, best) = KarmaRepository.ComputeStreaks(allLogs, today);
        var todayLog = allLogs.FirstOrDefault(l => l.Date == today);
        return Ok(ToResult(h, cur, best, todayLog?.Completed));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] HabitRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");
        var habit = new Habit
        {
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            Emoji = string.IsNullOrWhiteSpace(req.Emoji) ? "✅" : req.Emoji,
            Category = req.Category ?? "personal",
            NotifyTime = req.NotifyTime,
            NotifyMessage = req.NotifyMessage?.Trim(),
            NotifyChannel = req.NotifyChannel ?? "telegram",
            NotifyDays = req.NotifyDays ?? [0, 1, 2, 3, 4, 5, 6],
            GoalId = req.GoalId,
        };
        var saved = await repo.AddHabitAsync(habit);
        return Ok(ToResult(saved, 0, 0, null));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] HabitRequest req)
    {
        var updated = await repo.UpdateHabitAsync(id, h =>
        {
            h.Name = req.Name.Trim();
            h.Description = req.Description?.Trim();
            h.Emoji = string.IsNullOrWhiteSpace(req.Emoji) ? h.Emoji : req.Emoji;
            h.Category = req.Category ?? h.Category;
            h.NotifyTime = req.NotifyTime;
            h.NotifyMessage = req.NotifyMessage?.Trim();
            h.NotifyChannel = req.NotifyChannel ?? h.NotifyChannel;
            if (req.NotifyDays != null) h.NotifyDays = req.NotifyDays;
            h.GoalId = req.GoalId;
        });
        if (updated is null) return NotFound();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var allLogs = await repo.GetHabitLogsAsync(updated.Id, today.AddDays(-365), today);
        var (cur, best) = KarmaRepository.ComputeStreaks(allLogs, today);
        return Ok(ToResult(updated, cur, best, null));
    }

    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] bool active)
    {
        var updated = await repo.UpdateHabitAsync(id, h => h.IsActive = active);
        return updated is null ? NotFound() : Ok(new { updated.Id, updated.IsActive });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteHabitAsync(id) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/log")]
    public async Task<IActionResult> Log(Guid id, [FromBody] HabitLogRequest req)
    {
        var habit = await repo.GetHabitAsync(id);
        if (habit is null) return NotFound("Habit not found.");
        var date = req.Date ?? DateOnly.FromDateTime(DateTime.Now);
        var log = await repo.UpsertHabitLogAsync(id, date, req.Completed, req.Note);
        return Ok(new HabitLogResult(log.Id, log.HabitId, log.Date, log.Completed, log.Note, log.LoggedAt));
    }

    [HttpGet("{id:guid}/logs")]
    public async Task<IActionResult> GetLogs(Guid id, [FromQuery] int days = 90)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-days);
        var logs = await repo.GetHabitLogsAsync(id, from, today);
        return Ok(logs.Select(l => new HabitLogResult(l.Id, l.HabitId, l.Date, l.Completed, l.Note, l.LoggedAt)));
    }

    // Analytics for the calendar-heatmap + day-of-week stats view.
    [HttpGet("{id:guid}/stats")]
    public async Task<IActionResult> Stats(Guid id, [FromQuery] int days = 365)
    {
        var habit = await repo.GetHabitAsync(id);
        if (habit is null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-days);
        var logs = await repo.GetHabitLogsAsync(id, from, today);
        var (cur, best) = KarmaRepository.ComputeStreaks(logs, today);

        var totalLogged = logs.Count;
        var totalCompleted = logs.Count(l => l.Completed);
        var dow = new int[7];
        foreach (var l in logs.Where(l => l.Completed))
            dow[(int)l.Date.DayOfWeek]++;

        return Ok(new HabitStatsResult(
            id, totalLogged, totalCompleted,
            totalLogged > 0 ? (double)totalCompleted / totalLogged : 0,
            cur, best, dow,
            logs.Select(l => new HabitLogResult(l.Id, l.HabitId, l.Date, l.Completed, l.Note, l.LoggedAt)).ToList()));
    }

    private static HabitResult ToResult(Habit h, int cur, int best, bool? todayCompleted) =>
        new(h.Id, h.Name, h.Description, h.Emoji, h.Category,
            h.NotifyTime, h.NotifyMessage, h.NotifyChannel, h.NotifyDays,
            h.IsActive, cur, best, todayCompleted, h.CreatedAt, h.GoalId);
}
