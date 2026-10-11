using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NorthStar.Application.DTOs;
using NorthStar.Domain.Entities;

namespace NorthStar.Infrastructure.Data;

// Agent memory: writing with a lifecycle, recall, and correction.
//
// The rules, all enforced here rather than trusted to callers:
//   - The same memory is never stored twice. Restating it returns the one already held,
//     and restating it with authority confirms it if it was only proposed.
//   - Nothing is overwritten. A correction is a new memory that supersedes the old one.
//   - An unconfirmed claim never displaces a confirmed one. A proposed correction of an
//     active memory waits, and replaces it only when it is itself confirmed.
//   - Only live memories (active, proposed) are recalled. Superseded and retracted ones
//     are history, kept so "what did we believe before, and why did it change?" can be
//     answered.
public partial class NorthStarRepository
{
    // Kept for callers that insert a memory as-is (tests, imports). Agents go through
    // RememberAsync, which applies the rules above.
    public async Task<MemoryEntry> SaveMemoryAsync(MemoryEntry memory)
    {
        memory.ContentKey = MemorySearch.Normalise(memory.Content);
        db.Memories.Add(memory);
        await db.SaveChangesAsync();
        return memory;
    }

    public async Task<MemoryWriteResult> RememberAsync(MemoryEntry draft, IReadOnlyList<string>? entityNames = null)
    {
        draft.Content = draft.Content.Trim();
        draft.ContentKey = MemorySearch.Normalise(draft.Content);
        // A new memory starts live; history states are reached, never written directly.
        if (!MemoryStatus.IsLive(draft.Status)) draft.Status = MemoryStatus.Proposed;
        var now = DateTime.UtcNow;

        MemoryEntry? target = null;
        if (draft.Supersedes is { } targetId)
        {
            target = await db.Memories.FirstOrDefaultAsync(m => m.Id == targetId);
            if (target is null) return MemoryWriteResult.Fail($"memory {targetId} not found");
            if (!MemoryStatus.IsLive(target.Status))
                return MemoryWriteResult.Fail($"memory {targetId} is already {target.Status}");
        }

        var existing = (await db.Memories
                .Where(m => m.ContentKey == draft.ContentKey
                            && (m.Status == MemoryStatus.Active || m.Status == MemoryStatus.Proposed))
                .ToListAsync())
            .OrderBy(m => m.Status == MemoryStatus.Active ? 0 : 1)
            .FirstOrDefault();

        if (existing is not null && draft.ContentKey.Length > 0)
        {
            string note = "already remembered";
            if (draft.Status == MemoryStatus.Active && existing.Status == MemoryStatus.Proposed)
            {
                await ConfirmCoreAsync(existing, $"confirmed by restatement ({draft.Source})", now);
                note = "already remembered as proposed; now confirmed";
            }
            await db.SaveChangesAsync();
            await LinkEntitiesAsync(existing, entityNames, draft.Source);
            return new MemoryWriteResult(existing, true, [], note, null);
        }

        draft.StatusChangedAt = now;
        db.Memories.Add(draft);

        string? pending = null;
        if (target is not null)
        {
            if (draft.Status == MemoryStatus.Active || target.Status == MemoryStatus.Proposed)
                Supersede(target, draft, now);
            else
                pending = "proposed correction: the confirmed memory it replaces stays in force until this one is confirmed";
        }

        await db.SaveChangesAsync();
        await LinkEntitiesAsync(draft, entityNames, draft.Source);
        if (target is not null) await InheritEntitiesAsync(target, draft);

        var exclude = new HashSet<Guid> { draft.Id };
        if (draft.Supersedes is { } s) exclude.Add(s);
        var similar = await RecallCoreAsync(draft.Content, draft.Kind, 3, reinforce: false, exclude);
        return new MemoryWriteResult(draft, false, similar, pending, null);
    }

    public async Task<(MemoryEntry? Memory, string? Error)> ConfirmMemoryAsync(Guid id, string? note = null)
    {
        var m = await db.Memories.FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return (null, "not found");
        if (m.Status == MemoryStatus.Active) return (m, null);
        if (m.Status != MemoryStatus.Proposed)
            return (null, $"memory is {m.Status}; only a proposed memory can be confirmed");

        await ConfirmCoreAsync(m, string.IsNullOrWhiteSpace(note) ? "confirmed" : note.Trim(), DateTime.UtcNow);
        await db.SaveChangesAsync();
        return (m, null);
    }

    public async Task<(MemoryEntry? Memory, string? Error)> RetractMemoryAsync(Guid id, string? reason = null)
    {
        var m = await db.Memories.FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return (null, "not found");
        if (!MemoryStatus.IsLive(m.Status)) return (null, $"memory is already {m.Status}");

        var now = DateTime.UtcNow;
        m.Status = MemoryStatus.Retracted;
        m.StatusChangedAt = now;
        m.StatusNote = string.IsNullOrWhiteSpace(reason) ? "retracted" : reason.Trim();

        // Withdrawing a correction must not leave a hole where the corrected memory was.
        // What it replaced comes back -- but as proposed, because a claim that was once
        // replaced is not automatically trustworthy again just because its replacement
        // was wrong too.
        var replaced = await db.Memories
            .Where(x => x.SupersededBy == id && x.Status == MemoryStatus.Superseded)
            .ToListAsync();
        foreach (var r in replaced)
        {
            r.Status = MemoryStatus.Proposed;
            r.SupersededBy = null;
            r.StatusChangedAt = now;
            r.StatusNote = $"restored for review: its replacement {id} was retracted";
        }

        await db.SaveChangesAsync();
        return (m, null);
    }

    public async Task<MemoryDetail?> GetMemoryDetailAsync(Guid id)
    {
        var m = await db.Memories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return null;

        var seen = new HashSet<Guid> { m.Id };
        var before = new List<MemoryEntry>();
        for (var cur = m; cur.Supersedes is { } prevId && seen.Add(prevId);)
        {
            var prev = await db.Memories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == prevId);
            if (prev is null) break;
            before.Add(prev);
            cur = prev;
        }
        var after = new List<MemoryEntry>();
        for (var cur = m; cur.SupersededBy is { } nextId && seen.Add(nextId);)
        {
            var next = await db.Memories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == nextId);
            if (next is null) break;
            after.Add(next);
            cur = next;
        }
        before.Reverse();
        var history = before.Append(m).Concat(after).ToList();

        var memoryId = IdOf(m.Id);
        var entityIds = await db.Links.AsNoTracking()
            .Where(l => l.FromType == RecordTypes.Memory && l.FromId == memoryId && l.ToType == RecordTypes.Entity)
            .Select(l => l.ToId).ToListAsync();
        var entities = new List<NamedEntity>();
        foreach (var eid in entityIds.Distinct())
            if (Guid.TryParse(eid, out var g) && await db.Entities.AsNoTracking().FirstOrDefaultAsync(e => e.Id == g) is { } e)
                entities.Add(e);

        return new MemoryDetail(m, history, entities);
    }

    public Task<List<MemoryEntry>> RecallMemoriesAsync(string query, string? kind, int limit) =>
        RecallCoreAsync(query, kind, limit, reinforce: true, exclude: null);

    // status: null = live (active + proposed), "all", or one status.
    public async Task<List<MemoryEntry>> GetRecentMemoriesAsync(int limit, string? kind, string? status = null)
    {
        var q = db.Memories.AsQueryable();
        if (kind is not null) q = q.Where(m => m.Kind == kind);
        if (status is null) q = q.Where(m => m.Status == MemoryStatus.Active || m.Status == MemoryStatus.Proposed);
        else if (status != "all") q = q.Where(m => m.Status == status);
        return await q.OrderByDescending(m => m.CreatedAt).Take(limit).AsNoTracking().ToListAsync();
    }

    public async Task<bool> DeleteMemoryAsync(Guid id)
    {
        var m = await db.Memories.FindAsync(id);
        if (m is null) return false;
        var key = IdOf(id);
        db.Links.RemoveRange(db.Links.Where(l =>
            (l.FromType == RecordTypes.Memory && l.FromId == key) || (l.ToType == RecordTypes.Memory && l.ToId == key)));
        db.Memories.Remove(m);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<(int Total, Dictionary<string, int> ByKind, Dictionary<string, int> ByStatus)> GetMemoryStatsAsync()
    {
        var live = db.Memories.Where(m => m.Status == MemoryStatus.Active || m.Status == MemoryStatus.Proposed);
        var byKind = await live.GroupBy(m => m.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Kind, x => x.Count);
        var byStatus = await db.Memories.GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);
        return (byKind.Values.Sum(), byKind, byStatus);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static void Supersede(MemoryEntry target, MemoryEntry replacement, DateTime now)
    {
        target.Status = MemoryStatus.Superseded;
        target.SupersededBy = replacement.Id;
        target.StatusChangedAt = now;
        target.StatusNote = $"superseded by {replacement.Id}";
    }

    // Confirming a proposed correction is the moment it takes effect.
    private async Task ConfirmCoreAsync(MemoryEntry m, string note, DateTime now)
    {
        m.Status = MemoryStatus.Active;
        m.StatusChangedAt = now;
        m.StatusNote = note;
        if (m.Supersedes is { } targetId
            && await db.Memories.FirstOrDefaultAsync(x => x.Id == targetId) is { } target
            && MemoryStatus.IsLive(target.Status))
            Supersede(target, m, now);
    }

    private async Task<List<MemoryEntry>> RecallCoreAsync(
        string query, string? kind, int limit, bool reinforce, IReadOnlySet<Guid>? exclude)
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
                  AND m.Status IN ('active', 'proposed')
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

        var relevance = new Dictionary<Guid, double>();
        var normalised = MemorySearch.NormaliseBm25(hits.Select(h => h.Bm25).ToList());
        for (var i = 0; i < hits.Count; i++) relevance[hits[i].Id] = normalised[i];

        // Memories about an entity the message names, by name or alias, whether or not
        // they share a word with it: "my car" reaches the memory that only says Honda.
        foreach (var id in await MemoriesAboutEntitiesNamedInAsync(query))
            relevance[id] = Math.Max(relevance.GetValueOrDefault(id), MemorySearch.EntityRelevance);

        if (exclude is not null)
            foreach (var id in exclude) relevance.Remove(id);

        var results = new List<MemoryEntry>();
        if (relevance.Count > 0)
        {
            var ids = relevance.Keys.ToList();
            var now = DateTime.UtcNow;
            results = (await db.Memories.Where(m => ids.Contains(m.Id)).AsNoTracking().ToListAsync())
                .Where(m => MemoryStatus.IsLive(m.Status) && (kind is null || m.Kind == kind))
                .OrderByDescending(m => MemorySearch.Score(
                    relevance[m.Id], m.Importance, m.CreatedAt, m.AccessCount, now, m.Status))
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
                var q = db.Memories.Where(m => (m.Status == MemoryStatus.Active || m.Status == MemoryStatus.Proposed)
                    && (m.Content.ToLower().Contains(word) || m.Tags.ToLower().Contains(word)));
                if (kind is not null) q = q.Where(m => m.Kind == kind);
                results.AddRange(await q.OrderByDescending(m => m.Importance)
                    .ThenByDescending(m => m.CreatedAt)
                    .Take(limit).AsNoTracking().ToListAsync());
                if (results.Count >= limit) break;
            }
            results = results.Where(m => exclude is null || !exclude.Contains(m.Id))
                .DistinctBy(m => m.Id).Take(limit).ToList();
        }

        // Reinforcement: recalled memories get touched, so "hot" memories are observable.
        // Not for internal look-ups (the similar-memory check on save), which are not the
        // memory being used.
        if (reinforce && results.Count > 0)
        {
            var ids = results.Select(r => r.Id).ToList();
            await db.Memories.Where(m => ids.Contains(m.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.LastAccessedAt, DateTime.UtcNow)
                    .SetProperty(m => m.AccessCount, m => m.AccessCount + 1));
        }
        return results;
    }

    // A correction is about what the memory it corrects was about. Without this, fixing
    // "blue Civic" to "grey Civic" would cut the corrected memory off from the Honda
    // entity, and asking about "my car" would stop finding it. Inferred, not observed:
    // the new text may not name the entity itself.
    private async Task InheritEntitiesAsync(MemoryEntry from, MemoryEntry to)
    {
        var fromId = IdOf(from.Id);
        var entityIds = await db.Links.AsNoTracking()
            .Where(l => l.FromType == RecordTypes.Memory && l.FromId == fromId && l.ToType == RecordTypes.Entity)
            .Select(l => l.ToId).Distinct().ToListAsync();
        foreach (var entityId in entityIds)
            await AddLinkAsync(new RecordLink
            {
                FromType = RecordTypes.Memory, FromId = IdOf(to.Id),
                Relation = LinkRelations.Mentions,
                ToType = RecordTypes.Entity, ToId = entityId,
                Basis = LinkBasis.Inferred, Source = to.Source, SourceRef = $"inherited from memory {fromId}",
            });
    }

    private async Task<List<Guid>> MemoriesAboutEntitiesNamedInAsync(string query)
    {
        var entityIds = (await EntitiesNamedInAsync(query)).Select(e => IdOf(e.Id)).ToList();
        if (entityIds.Count == 0) return [];

        var memoryIds = await db.Links.AsNoTracking()
            .Where(l => (l.FromType == RecordTypes.Memory && l.ToType == RecordTypes.Entity && entityIds.Contains(l.ToId))
                     || (l.ToType == RecordTypes.Memory && l.FromType == RecordTypes.Entity && entityIds.Contains(l.FromId)))
            .Select(l => l.FromType == RecordTypes.Memory ? l.FromId : l.ToId)
            .ToListAsync();

        return memoryIds.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().ToList();
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // Record ids inside links are strings (a fact's id is its key); Guids are always
    // written in one form so a link can be found again by string comparison.
    private static string IdOf(Guid id) => id.ToString("D");
}
