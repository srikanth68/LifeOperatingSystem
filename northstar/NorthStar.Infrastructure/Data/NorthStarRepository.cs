using Microsoft.EntityFrameworkCore;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.Infrastructure.Data;

public partial class NorthStarRepository(NorthStarDbContext db) : INorthStarRepository
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

    // Returns the entry and whether it was newly created, so callers can tell a fresh
    // fact from a restatement of one already held.
    public async Task<(KnowledgeEntry Entry, bool Created)> UpsertDailyEntryAsync(
        string source, string topic, string summary, string? rawJson, DateOnly day)
    {
        var existing = await db.Entries.FirstOrDefaultAsync(e => e.Source == source && e.Topic == topic && e.Day == day);
        if (existing is not null)
        {
            existing.Summary = summary;
            // Only when the caller actually supplied one — a later write that carries no
            // raw payload must not erase the payload an earlier write stored.
            if (rawJson is not null) existing.RawJson = rawJson;
            existing.CreatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return (existing, false);
        }
        var entry = new KnowledgeEntry { Source = source, Topic = topic, Summary = summary, RawJson = rawJson, Day = day, CreatedAt = DateTime.UtcNow };
        db.Entries.Add(entry);
        await db.SaveChangesAsync();
        return (entry, true);
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

    public async Task<bool> DeleteActionAsync(Guid id)
    {
        var a = await db.Actions.FindAsync(id);
        if (a is null) return false;
        db.Actions.Remove(a);
        await db.SaveChangesAsync();
        return true;
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
    // A fact holds one current value; every value it is moved off, by a change or a
    // delete, is kept as a FactRevision. Setting the same value again changes nothing.
    public async Task UpsertFactAsync(UserFact fact)
    {
        var now = DateTime.UtcNow;
        var existing = await db.Facts.FindAsync(fact.Key);
        if (existing is not null)
        {
            if (existing.Value != fact.Value)
                db.FactRevisions.Add(RevisionOf(existing, now, FactChange.Replaced));
            existing.Value = fact.Value;
            existing.Source = fact.Source;
            existing.SourceRef = fact.SourceRef;
            existing.UpdatedAt = now;
        }
        else
        {
            fact.UpdatedAt = now;
            db.Facts.Add(fact);
        }
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
        db.FactRevisions.Add(RevisionOf(f, DateTime.UtcNow, FactChange.Deleted));
        db.Facts.Remove(f);
        await db.SaveChangesAsync();
        return true;
    }

    // Newest first: what it was most recently, then before that.
    public Task<List<FactRevision>> GetFactHistoryAsync(string key) =>
        db.FactRevisions.Where(r => r.Key == key)
            .OrderByDescending(r => r.ValidTo).ThenByDescending(r => r.Id)
            .AsNoTracking().ToListAsync();

    private static FactRevision RevisionOf(UserFact f, DateTime now, string change) => new()
    {
        Key = f.Key, Value = f.Value, Source = f.Source, SourceRef = f.SourceRef,
        ValidFrom = f.UpdatedAt, ValidTo = now, Change = change,
    };

    // ── Activity events (append-only, event-level) ──
    public async Task<bool> AddEventIfNewAsync(ActivityEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.EventKey))
            ev.EventKey = $"{ev.Source}:{ev.Kind}:{ev.Id}"; // last-resort unique key
        if (await db.Events.AnyAsync(e => e.EventKey == ev.EventKey))
            return false;
        ev.RecordedAt = DateTime.UtcNow;
        db.Events.Add(ev);
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Lost a race to another producer inserting the same EventKey — the unique
            // index rejected it. That's the idempotency guarantee doing its job.
            db.Entry(ev).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<int> AddEventsIfNewAsync(IEnumerable<ActivityEvent> evs)
    {
        var inserted = 0;
        foreach (var ev in evs)
            if (await AddEventIfNewAsync(ev)) inserted++;
        return inserted;
    }

    public async Task<List<ActivityEvent>> GetEventsSinceAsync(DateTime sinceUtc, string? source, int limit)
    {
        var q = db.Events.Where(e => e.OccurredAt > sinceUtc);
        if (source is not null) q = q.Where(e => e.Source == source);
        return await q.OrderByDescending(e => e.OccurredAt).Take(limit).AsNoTracking().ToListAsync();
    }
}
