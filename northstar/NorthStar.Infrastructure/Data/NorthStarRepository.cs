using System.Data;
using System.Data.Common;
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

    // ── Agent memory (FTS5) ──
    public async Task<MemoryEntry> SaveMemoryAsync(MemoryEntry memory)
    {
        db.Memories.Add(memory);
        await db.SaveChangesAsync();
        return memory;
    }

    public async Task<List<MemoryEntry>> RecallMemoriesAsync(string query, string? kind, int limit)
    {
        // Topic words only, stemmed and prefix-matched, and never raw user text in the
        // MATCH expression -- see MemorySearch. A message with no topic words recalls
        // nothing rather than everything that shares an "is".
        var match = MemorySearch.BuildMatch(query);
        if (match is null) return [];

        // Candidates by text relevance, generously, then re-ranked on relevance together
        // with importance, recency and reinforcement. bm25 is read out alongside the id
        // because the blend needs the score itself, not just the order.
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        var hits = new List<(Guid Id, double Bm25)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT m.Id, bm25(MemoryFts) FROM MemoryFts
                JOIN Memories m ON m.rowid = MemoryFts.rowid
                WHERE MemoryFts MATCH $match AND ($kind IS NULL OR m.Kind = $kind)
                ORDER BY bm25(MemoryFts)
                LIMIT $take
                """;
            AddParameter(cmd, "$match", match);
            AddParameter(cmd, "$kind", (object?)kind ?? DBNull.Value);
            AddParameter(cmd, "$take", Math.Max(limit * 4, 20));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                hits.Add((reader.GetGuid(0), reader.GetDouble(1)));
        }

        var results = new List<MemoryEntry>();
        if (hits.Count > 0)
        {
            var ids = hits.Select(h => h.Id).ToList();
            var byId = (await db.Memories.Where(m => ids.Contains(m.Id)).AsNoTracking().ToListAsync())
                .ToDictionary(m => m.Id);
            var relevance = MemorySearch.NormaliseBm25(hits.Select(h => h.Bm25).ToList());
            var now = DateTime.UtcNow;

            results = hits
                .Select((h, i) => (Entry: byId.GetValueOrDefault(h.Id), Relevance: relevance[i]))
                .Where(x => x.Entry is not null)
                .OrderByDescending(x => MemorySearch.Score(
                    x.Relevance, x.Entry!.Importance, x.Entry.CreatedAt, x.Entry.AccessCount, now))
                .Select(x => x.Entry!)
                // The same memory distilled twice is one thing to know, not two lines.
                .DistinctBy(m => MemorySearch.Normalise(m.Content))
                .Take(limit)
                .ToList();
        }

        // Fallback when the index finds nothing, for a fragment inside a longer word:
        // a substring scan per topic word, most important first.
        if (results.Count == 0)
        {
            foreach (var word in MemorySearch.Keywords(query).Where(w => w.Length >= 3))
            {
                var q = db.Memories.Where(m => m.Content.ToLower().Contains(word) || m.Tags.ToLower().Contains(word));
                if (kind is not null) q = q.Where(m => m.Kind == kind);
                results.AddRange(await q.OrderByDescending(m => m.Importance)
                    .ThenByDescending(m => m.CreatedAt)
                    .Take(limit).AsNoTracking().ToListAsync());
                if (results.Count >= limit) break;
            }
            results = results.DistinctBy(m => m.Id).Take(limit).ToList();
        }

        // Reinforcement: recalled memories get touched, so "hot" memories are observable.
        if (results.Count > 0)
        {
            var ids = results.Select(r => r.Id).ToList();
            await db.Memories.Where(m => ids.Contains(m.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LastAccessedAt, DateTime.UtcNow)
                    .SetProperty(m => m.AccessCount, m => m.AccessCount + 1));
        }
        return results;
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    public async Task<List<MemoryEntry>> GetRecentMemoriesAsync(int limit, string? kind)
    {
        var q = db.Memories.AsQueryable();
        if (kind is not null) q = q.Where(m => m.Kind == kind);
        return await q.OrderByDescending(m => m.CreatedAt).Take(limit).AsNoTracking().ToListAsync();
    }

    public async Task<bool> DeleteMemoryAsync(Guid id)
    {
        var m = await db.Memories.FindAsync(id);
        if (m is null) return false;
        db.Memories.Remove(m);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<(int Total, Dictionary<string, int> ByKind)> GetMemoryStatsAsync()
    {
        var byKind = await db.Memories.GroupBy(m => m.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Kind, x => x.Count);
        return (byKind.Values.Sum(), byKind);
    }
}
