using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.DTOs;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/dashboard")]
public class DashboardController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var modules = new[] { "vault", "vitara", "aasthi", "san" };

        var entriesBySource = new Dictionary<string, int>();
        foreach (var m in modules)
            entriesBySource[m] = await repo.GetEntryCountAsync(m);
        entriesBySource["manual"] = await repo.GetEntryCountAsync("manual");

        var recent = await repo.GetEntriesAsync(days: 30, limit: 50);
        var entriesByTopic = recent.GroupBy(e => e.Topic).ToDictionary(g => g.Key, g => g.Count());

        var insights = await repo.GetInsightsAsync(limit: 5);
        var recentEntries = await repo.GetTimelineAsync(days: 7, limit: 10);

        var lastSync = new Dictionary<string, DateTime?>();
        foreach (var m in modules)
        {
            var sync = await repo.GetModuleSyncAsync(m);
            lastSync[m] = sync?.LastSyncAt;
        }

        var total = await repo.GetEntryCountAsync();

        return Ok(new DashboardResult(
            total,
            entriesBySource,
            entriesByTopic,
            insights.Select(i => new InsightResult(i.Id, i.Title, i.Body, i.GeneratedBy, i.Dismissed, i.CreatedAt)).ToList(),
            recentEntries.Select(e => new KnowledgeEntryResult(e.Id, e.Source, e.Topic, e.Summary, e.Day?.ToString("yyyy-MM-dd"), e.CreatedAt)).ToList(),
            lastSync
        ));
    }
}
