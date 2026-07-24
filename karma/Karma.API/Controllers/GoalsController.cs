using System.Text.Json;
using Karma.Application.DTOs;
using Karma.Application.Interfaces;
using Karma.Domain.Entities;
using Karma.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace Karma.API.Controllers;

[ApiController, Route("api/goals")]
public class GoalsController(IKarmaRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? category)
    {
        var goals = await repo.GetGoalsAsync(status, category);
        var results = new List<GoalResult>();
        foreach (var g in goals)
        {
            var milestones = await repo.GetMilestonesAsync(g.Id);
            results.Add(ToResult(g, milestones));
        }
        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var g = await repo.GetGoalAsync(id);
        if (g is null) return NotFound();
        var milestones = await repo.GetMilestonesAsync(id);
        return Ok(ToResult(g, milestones));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GoalRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        var goal = new Goal
        {
            Title = req.Title.Trim(),
            Description = req.Description?.Trim(),
            Category = req.Category ?? "personal",
            Status = req.Status ?? "active",
            Progress = Math.Clamp(req.Progress ?? 0, 0, 100),
            TargetDate = req.TargetDate,
            LinksJson = req.Links is { Count: > 0 } ? JsonSerializer.Serialize(req.Links) : null,
            Resources = req.Resources?.Trim(),
            Tags = req.Tags?.Trim(),
        };
        var saved = await repo.AddGoalAsync(goal);
        return Ok(ToResult(saved, []));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] GoalRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        var updated = await repo.UpdateGoalAsync(id, g =>
        {
            g.Title = req.Title.Trim();
            g.Description = req.Description?.Trim();
            g.Category = req.Category ?? g.Category;
            g.Status = req.Status ?? g.Status;
            g.Progress = Math.Clamp(req.Progress ?? g.Progress, 0, 100);
            g.TargetDate = req.TargetDate;
            g.LinksJson = req.Links is { Count: > 0 } ? JsonSerializer.Serialize(req.Links) : g.LinksJson;
            g.Resources = req.Resources?.Trim();
            g.Tags = req.Tags?.Trim();
            if (g.Status == "completed" && g.CompletedAt is null) g.CompletedAt = DateTime.UtcNow;
        });
        if (updated is null) return NotFound();
        var milestones = await repo.GetMilestonesAsync(id);
        return Ok(ToResult(updated, milestones));
    }

    [HttpPatch("{id:guid}/progress")]
    public async Task<IActionResult> SetProgress(Guid id, [FromBody] int progress)
    {
        var updated = await repo.UpdateGoalAsync(id, g =>
        {
            g.Progress = Math.Clamp(progress, 0, 100);
            if (g.Progress == 100 && g.Status == "active")
            {
                g.Status = "completed";
                g.CompletedAt = DateTime.UtcNow;
            }
        });
        return updated is null ? NotFound() : Ok(new { updated.Id, updated.Progress, updated.Status });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteGoalAsync(id) ? NoContent() : NotFound();

    // ── Milestones ───────────────────────────────────────────
    [HttpGet("{goalId:guid}/milestones")]
    public async Task<IActionResult> GetMilestones(Guid goalId) =>
        Ok((await repo.GetMilestonesAsync(goalId)).Select(ToMilestone));

    [HttpPost("{goalId:guid}/milestones")]
    public async Task<IActionResult> AddMilestone(Guid goalId, [FromBody] MilestoneRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest("Title is required.");
        var goal = await repo.GetGoalAsync(goalId);
        if (goal is null) return NotFound("Goal not found.");
        var m = new GoalMilestone { GoalId = goalId, Title = req.Title.Trim(), TargetDate = req.TargetDate };
        var saved = await repo.AddMilestoneAsync(m);
        await RecomputeProgressFromMilestonesAsync(goalId);
        return Ok(ToMilestone(saved));
    }

    [HttpPatch("{goalId:guid}/milestones/{milestoneId:guid}")]
    public async Task<IActionResult> ToggleMilestone(Guid goalId, Guid milestoneId, [FromBody] bool completed)
    {
        var updated = await repo.UpdateMilestoneAsync(milestoneId, m =>
        {
            m.Completed = completed;
            m.CompletedAt = completed ? DateTime.UtcNow : null;
        });
        if (updated is null) return NotFound();
        await RecomputeProgressFromMilestonesAsync(goalId);
        return Ok(ToMilestone(updated));
    }

    [HttpDelete("{goalId:guid}/milestones/{milestoneId:guid}")]
    public async Task<IActionResult> DeleteMilestone(Guid goalId, Guid milestoneId)
    {
        if (!await repo.DeleteMilestoneAsync(milestoneId)) return NotFound();
        await RecomputeProgressFromMilestonesAsync(goalId);
        return NoContent();
    }

    // Habits linked to this goal, with their recent completion rate (informational —
    // does NOT feed Goal.Progress, which is derived from milestones).
    [HttpGet("{goalId:guid}/habits")]
    public async Task<IActionResult> LinkedHabits(Guid goalId)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var habits = (await repo.GetHabitsAsync()).Where(h => h.GoalId == goalId).ToList();
        var results = new List<LinkedHabitResult>();
        foreach (var h in habits)
        {
            var logs = await repo.GetHabitLogsAsync(h.Id, today.AddDays(-365), today);
            var (cur, _) = KarmaRepository.ComputeStreaks(logs, today);
            var last7 = logs.Where(l => l.Date > today.AddDays(-7)).ToList();
            var rate = last7.Count > 0 ? (double)last7.Count(l => l.Completed) / 7 : 0;
            results.Add(new LinkedHabitResult(h.Id, h.Name, h.Emoji, cur, rate));
        }
        return Ok(results);
    }

    // When a goal has milestones, its Progress is the % of milestones completed.
    private async Task RecomputeProgressFromMilestonesAsync(Guid goalId)
    {
        var milestones = await repo.GetMilestonesAsync(goalId);
        if (milestones.Count == 0) return;   // manual progress preserved for milestone-free goals
        var pct = (int)Math.Round((double)milestones.Count(m => m.Completed) / milestones.Count * 100);
        await repo.UpdateGoalAsync(goalId, g =>
        {
            g.Progress = pct;
            if (pct == 100 && g.Status == "active") { g.Status = "completed"; g.CompletedAt = DateTime.UtcNow; }
            else if (pct < 100 && g.Status == "completed") { g.Status = "active"; g.CompletedAt = null; }
        });
    }

    private static List<GoalLink> ParseLinks(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<GoalLink>>(json) ?? [];

    private static MilestoneResult ToMilestone(GoalMilestone m) =>
        new(m.Id, m.Title, m.TargetDate, m.Completed, m.CompletedAt);

    private static GoalResult ToResult(Goal g, List<GoalMilestone> milestones) =>
        new(g.Id, g.Title, g.Description, g.Category, g.Status, g.Progress,
            g.TargetDate, ParseLinks(g.LinksJson), g.Resources, g.Tags,
            milestones.Select(ToMilestone).ToList(), g.CreatedAt, g.CompletedAt);
}
