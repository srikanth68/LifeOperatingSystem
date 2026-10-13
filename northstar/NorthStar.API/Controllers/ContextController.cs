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

    // What NorthStar already holds that relates to a piece of incoming text -- an email,
    // today. San asks before deciding what an email means, so a new message about the
    // flat is read beside what is known about the flat, and a reply is read beside the
    // earlier messages in its thread. Three independent lookups, each allowed to be
    // empty: entities the text names (by name or alias) with what is linked to them,
    // memories that match it, and earlier events from the same sender or thread.
    [HttpPost("related")]
    public async Task<IActionResult> Related([FromBody] RelatedRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest("text is required");
        var limit = Math.Clamp(req.Limit ?? 6, 1, 20);

        var entities = new List<object>();
        foreach (var e in (await repo.EntitiesNamedInAsync(req.Text)).Take(limit))
        {
            var links = await repo.GetLinksAsync(Domain.Entities.RecordTypes.Entity, e.Id.ToString());
            entities.Add(new
            {
                e.Id, e.Name, e.Type, e.Aliases,
                linked = links.Where(l => l.OtherSummary is not null).Take(limit)
                    .Select(l => new { l.OtherType, l.OtherId, summary = l.OtherSummary, l.Basis }),
            });
        }

        var memories = await repo.RecallMemoriesAsync(req.Text, null, limit);

        var needles = (req.Senders ?? []).Append(req.Thread ?? "").ToList();
        var earlier = await repo.FindEventsAsync(req.Source ?? "email", needles, 365, limit);

        return Ok(new
        {
            entities,
            memories = memories.Select(m => new { m.Id, m.Content, m.Kind, m.Status, m.CreatedAt }),
            earlier = earlier.Select(e => new { e.Id, e.Title, e.Detail, e.OccurredAt }),
        });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncModules()
    {
        var results = await syncService.SyncAllAsync();
        return Ok(new { syncedAt = DateTime.UtcNow, results });
    }
}

// Text: what to relate (an email's subject and body). Senders and Thread find earlier
// events from the same correspondent or conversation; Source is whose events to search.
public record RelatedRequest(string Text, List<string>? Senders = null, string? Thread = null, string? Source = null, int? Limit = null);
