using Microsoft.EntityFrameworkCore;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.Infrastructure.Data;

public class NorthStarRepository(NorthStarDbContext db) : INorthStarRepository
{
    // ── Knowledge ──
    public async Task<KnowledgeEntry> AddEntryAsync(KnowledgeEntry entry)
    {
        db.Entries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<List<KnowledgeEntry>> GetEntriesAsync(string? source, string? topic, int days, int limit)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var q = db.Entries.Where(e => e.CreatedAt >= cutoff);
        if (source is not null) q = q.Where(e => e.Source == source);
        if (topic is not null) q = q.Where(e => e.Topic == topic);
        return await q.OrderByDescending(e => e.CreatedAt).Take(limit).ToListAsync();
    }

    public Task<KnowledgeEntry?> GetEntryAsync(Guid id) =>
        db.Entries.FirstOrDefaultAsync(e => e.Id == id);

    public async Task<List<KnowledgeEntry>> SearchAsync(string query, int limit)
    {
        var lower = query.ToLowerInvariant();
        return await db.Entries
            .Where(e => e.Summary.ToLower().Contains(lower) || e.Topic.ToLower().Contains(lower))
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetEntryCountAsync(string? source)
    {
        var q = db.Entries.AsQueryable();
        if (source is not null) q = q.Where(e => e.Source == source);
        return await q.CountAsync();
    }

    // ── Insights ──
    public async Task<Insight> AddInsightAsync(Insight insight)
    {
        db.Insights.Add(insight);
        await db.SaveChangesAsync();
        return insight;
    }

    public async Task<List<Insight>> GetInsightsAsync(bool includeDismissed, int limit)
    {
        var q = db.Insights.AsQueryable();
        if (!includeDismissed) q = q.Where(i => !i.Dismissed);
        return await q.OrderByDescending(i => i.CreatedAt).Take(limit).ToListAsync();
    }

    public async Task<Insight?> DismissInsightAsync(Guid id)
    {
        var i = await db.Insights.FirstOrDefaultAsync(x => x.Id == id);
        if (i is null) return null;
        i.Dismissed = true;
        await db.SaveChangesAsync();
        return i;
    }

    // ── Timeline ──
    public async Task<List<KnowledgeEntry>> GetTimelineAsync(int days, int limit)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return await db.Entries
            .Where(e => e.CreatedAt >= cutoff)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    // ── Module sync ──
    public Task<ModuleSync?> GetModuleSyncAsync(string module) =>
        db.ModuleSyncs.FirstOrDefaultAsync(m => m.Module == module);

    public Task<List<ModuleSync>> GetAllModuleSyncsAsync() =>
        db.ModuleSyncs.ToListAsync();

    public async Task UpsertModuleSyncAsync(ModuleSync sync)
    {
        var existing = await db.ModuleSyncs.FirstOrDefaultAsync(m => m.Module == sync.Module);
        if (existing is not null)
        {
            existing.LastSyncAt = sync.LastSyncAt;
            existing.LastError = sync.LastError;
        }
        else db.ModuleSyncs.Add(sync);
        await db.SaveChangesAsync();
    }

    // ── Actions ──
    public async Task<ActionItem> AddActionAsync(ActionItem action)
    {
        db.Actions.Add(action);
        await db.SaveChangesAsync();
        return action;
    }

    public async Task<List<ActionItem>> GetActionsAsync(string? status, int limit)
    {
        var q = db.Actions.AsQueryable();
        if (status is not null) q = q.Where(a => a.Status == status);
        return await q.OrderBy(a => a.Priority).ThenBy(a => a.DueDate).Take(limit).ToListAsync();
    }

    public async Task<ActionItem?> UpdateActionAsync(Guid id, string status, string? resolvedBy)
    {
        var a = await db.Actions.FindAsync(id);
        if (a is null) return null;
        a.Status = status;
        a.ResolvedBy = resolvedBy;
        if (status is "completed" or "dismissed") a.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return a;
    }

    // ── Module snapshots ──
    public async Task UpsertSnapshotAsync(ModuleSnapshot snapshot)
    {
        var existing = await db.Snapshots.FindAsync(snapshot.Module);
        if (existing is not null)
        {
            existing.SummaryJson = snapshot.SummaryJson;
            existing.CapturedAt = DateTime.UtcNow;
        }
        else db.Snapshots.Add(snapshot);
        await db.SaveChangesAsync();
    }

    public Task<ModuleSnapshot?> GetSnapshotAsync(string module) =>
        db.Snapshots.FindAsync(module).AsTask();

    public Task<List<ModuleSnapshot>> GetAllSnapshotsAsync() =>
        db.Snapshots.ToListAsync();

    // ── User facts ──
    public async Task UpsertFactAsync(UserFact fact)
    {
        var existing = await db.Facts.FindAsync(fact.Key);
        if (existing is not null)
        {
            existing.Value = fact.Value;
            existing.Source = fact.Source;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else db.Facts.Add(fact);
        await db.SaveChangesAsync();
    }

    public Task<UserFact?> GetFactAsync(string key) =>
        db.Facts.FindAsync(key).AsTask();

    public Task<List<UserFact>> GetAllFactsAsync() =>
        db.Facts.OrderBy(f => f.Key).ToListAsync();

    public async Task<bool> DeleteFactAsync(string key)
    {
        var f = await db.Facts.FindAsync(key);
        if (f is null) return false;
        db.Facts.Remove(f);
        await db.SaveChangesAsync();
        return true;
    }
}
