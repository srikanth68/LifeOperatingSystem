using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NorthStar.API.Services;
using NorthStar.Application.Interfaces;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/context")]
public class ContextController(INorthStarRepository repo, ModuleSyncService syncService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetContext()
    {
        var facts = await repo.GetAllFactsAsync();
        var syncs = await repo.GetAllModuleSyncsAsync();
        var snapshots = await repo.GetAllSnapshotsAsync();
        var actions = await repo.GetActionsAsync("pending", 20);
        var insights = await repo.GetInsightsAsync(false, 10);
        var recentKnowledge = await repo.GetTimelineAsync(7, 30);

        var snapshotMap = new Dictionary<string, object?>();
        foreach (var s in snapshots)
        {
            try { snapshotMap[s.Module] = JsonSerializer.Deserialize<object>(s.SummaryJson); }
            catch { snapshotMap[s.Module] = s.SummaryJson; }
        }

        return Ok(new
        {
            generatedAt = DateTime.UtcNow,
            user = facts.ToDictionary(f => f.Key, f => f.Value),
            modules = syncs.Select(s => new
            {
                name = s.Module,
                lastSync = s.LastSyncAt,
                healthy = s.LastError == null,
                error = s.LastError,
                snapshot = snapshotMap.GetValueOrDefault(s.Module),
            }),
            pendingActions = actions.Select(a => new
            {
                a.Id, a.Source, a.Category, a.Title, a.Description,
                a.Priority, dueDate = a.DueDate?.ToString("yyyy-MM-dd"),
            }),
            activeInsights = insights.Select(i => new { i.Id, i.Title, i.Body, i.GeneratedBy, i.CreatedAt }),
            recentKnowledge = recentKnowledge.Select(k => new
            {
                k.Source, k.Topic, k.Summary,
                day = k.Day?.ToString("yyyy-MM-dd"), k.CreatedAt,
            }),
        });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncModules()
    {
        var results = await syncService.SyncAllAsync();
        return Ok(new { syncedAt = DateTime.UtcNow, results });
    }
}
