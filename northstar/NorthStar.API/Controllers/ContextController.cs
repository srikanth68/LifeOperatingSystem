using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/context")]
public class ContextController(INorthStarRepository repo, IHttpClientFactory httpFactory) : ControllerBase
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
        var modules = new[] { "vault", "vitara", "aasthi", "san", "sutra" };
        var ports = new Dictionary<string, int>
        {
            ["vault"] = 5000, ["vitara"] = 5100, ["aasthi"] = 5200,
            ["san"] = 5300, ["sutra"] = 5400,
        };
        var endpoints = new Dictionary<string, string>
        {
            ["vault"] = "/api/summary",
            ["vitara"] = "/api/dashboard",
            ["aasthi"] = "/api/properties",
            ["san"] = "/api/people?limit=5",
            ["sutra"] = "/api/documents/stats",
        };

        var results = new Dictionary<string, object?>();
        var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        foreach (var mod in modules)
        {
            var url = $"http://localhost:{ports[mod]}{endpoints[mod]}";
            string? error = null;
            string? json = null;

            try
            {
                var resp = await client.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                    json = await resp.Content.ReadAsStringAsync();
                else
                    error = $"HTTP {(int)resp.StatusCode}";
            }
            catch (Exception ex)
            {
                error = ex.Message.Length > 100 ? ex.Message[..100] : ex.Message;
            }

            await repo.UpsertModuleSyncAsync(new() { Module = mod, LastSyncAt = DateTime.UtcNow, LastError = error });

            if (json is not null)
            {
                await repo.UpsertSnapshotAsync(new() { Module = mod, SummaryJson = json });
                results[mod] = "synced";
            }
            else
            {
                results[mod] = error;
            }
        }

        return Ok(new { syncedAt = DateTime.UtcNow, results });
    }
}
