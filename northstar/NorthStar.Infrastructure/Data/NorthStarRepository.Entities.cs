using Microsoft.EntityFrameworkCore;
using NorthStar.Application.DTOs;
using NorthStar.Domain.Entities;

namespace NorthStar.Infrastructure.Data;

// Entities and the links between records. NorthStar stores what is said to be related
// and on what basis; it does not decide that two things are related on its own.
public partial class NorthStarRepository
{
    // "vehicle:Honda Civic" names the type; "Honda Civic" leaves it to what is known.
    public static (string? Type, string Name) ParseEntityName(string raw)
    {
        var s = raw.Trim();
        var colon = s.IndexOf(':');
        if (colon > 0)
        {
            var type = s[..colon].Trim().ToLowerInvariant();
            if (EntityTypes.IsValid(type)) return (type, s[(colon + 1)..].Trim());
        }
        return (null, s);
    }

    // By name first, then by alias. Null for a name with no words in it.
    public async Task<NamedEntity?> ResolveEntityAsync(string name)
    {
        var key = MemorySearch.Normalise(name);
        if (key.Length == 0) return null;
        var byName = await db.Entities.FirstOrDefaultAsync(e => e.NameKey == key);
        if (byName is not null) return byName;
        var withAliases = await db.Entities.Where(e => e.Aliases != "").ToListAsync();
        return withAliases.FirstOrDefault(e => e.AllNames().Any(n => MemorySearch.Normalise(n) == key));
    }

    public async Task<(NamedEntity? Entity, bool Created, string? Error)> UpsertEntityAsync(
        string name, string? type = null, string? aliases = null, string? note = null)
    {
        name = name.Trim();
        if (MemorySearch.Normalise(name).Length == 0) return (null, false, "name is required");
        if (type is not null && !EntityTypes.IsValid(type))
            return (null, false, $"type must be one of: {string.Join(", ", EntityTypes.All)}");

        var now = DateTime.UtcNow;
        var known = await ResolveEntityAsync(name);
        if (known is not null)
        {
            // A known entity keeps the type it was given; "other" is not a choice anyone
            // made, so a real type replaces it.
            if (known.Type == EntityTypes.Other && type is not null) known.Type = type;
            known.Aliases = MergeAliases(known, aliases);
            if (known.Note is null && !string.IsNullOrWhiteSpace(note)) known.Note = note.Trim();
            known.UpdatedAt = now;
            await db.SaveChangesAsync();
            return (known, false, null);
        }

        var entity = new NamedEntity
        {
            Name = name,
            NameKey = MemorySearch.Normalise(name),
            Type = type ?? EntityTypes.Other,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        entity.Aliases = MergeAliases(entity, aliases);
        db.Entities.Add(entity);
        await db.SaveChangesAsync();
        return (entity, true, null);
    }

    public async Task<(NamedEntity? Entity, string? Error)> UpdateEntityAsync(
        Guid id, string? name, string? type, string? aliases, string? note)
    {
        var e = await db.Entities.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return (null, "not found");
        if (type is not null && !EntityTypes.IsValid(type))
            return (null, $"type must be one of: {string.Join(", ", EntityTypes.All)}");

        if (name is not null)
        {
            var key = MemorySearch.Normalise(name);
            if (key.Length == 0) return (null, "name cannot be empty");
            if (key != e.NameKey && await db.Entities.AnyAsync(x => x.NameKey == key && x.Id != id))
                return (null, $"another entity is already called \"{name.Trim()}\"");
            e.Name = name.Trim();
            e.NameKey = key;
        }
        if (type is not null) e.Type = type;
        // Aliases given here replace the list: this is the edit, not the merge.
        if (aliases is not null) e.Aliases = MergeAliases(new NamedEntity { Name = e.Name }, aliases);
        if (note is not null) e.Note = note.Trim().Length == 0 ? null : note.Trim();
        e.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (e, null);
    }

    public async Task<bool> DeleteEntityAsync(Guid id)
    {
        var e = await db.Entities.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return false;
        var key = IdOf(id);
        db.Links.RemoveRange(db.Links.Where(l =>
            (l.FromType == RecordTypes.Entity && l.FromId == key) || (l.ToType == RecordTypes.Entity && l.ToId == key)));
        db.Entities.Remove(e);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<NamedEntity>> FindEntitiesAsync(string? query, string? type = null, int limit = 50)
    {
        var all = await db.Entities.AsNoTracking().ToListAsync();
        var key = MemorySearch.Normalise(query ?? "");
        return all
            .Where(e => type is null || e.Type == type)
            .Where(e => key.Length == 0 || e.AllNames().Any(n => MemorySearch.Normalise(n).Contains(key)))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    // The entities a piece of text names, by whole name or alias. Names with no topic
    // words ("me", "it") are ignored: an entity called "it" would be named in every
    // sentence.
    public async Task<List<NamedEntity>> EntitiesNamedInAsync(string text)
    {
        var padded = $" {MemorySearch.Normalise(text)} ";
        if (padded.Trim().Length == 0) return [];
        var all = await db.Entities.AsNoTracking().ToListAsync();
        return all.Where(e => e.AllNames()
                .Select(MemorySearch.Normalise)
                .Any(n => MemorySearch.Keywords(n).Count > 0 && padded.Contains($" {n} ")))
            .ToList();
    }

    public async Task<EntityDetail?> GetEntityDetailAsync(Guid id)
    {
        var e = await db.Entities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return e is null ? null : new EntityDetail(e, await GetLinksAsync(RecordTypes.Entity, IdOf(id)));
    }

    // ── Links ──

    public async Task<(RecordLink? Link, bool Created, string? Error)> AddLinkAsync(RecordLink link)
    {
        if (!RecordTypes.IsValid(link.FromType) || !RecordTypes.IsValid(link.ToType))
            return (null, false, $"types must be one of: {string.Join(", ", RecordTypes.All)}");
        if (!LinkBasis.IsValid(link.Basis)) return (null, false, "basis must be observed or inferred");
        link.Relation = link.Relation.Trim().ToLowerInvariant();
        if (link.Relation.Length == 0) return (null, false, "relation is required");

        link.FromId = CanonicalId(link.FromType, link.FromId);
        link.ToId = CanonicalId(link.ToType, link.ToId);
        if (link.FromType == link.ToType && link.FromId == link.ToId)
            return (null, false, "a record cannot be linked to itself");
        if (await DescribeAsync(link.FromType, link.FromId) is null)
            return (null, false, $"{link.FromType} {link.FromId} not found");
        if (await DescribeAsync(link.ToType, link.ToId) is null)
            return (null, false, $"{link.ToType} {link.ToId} not found");

        var existing = await db.Links.FirstOrDefaultAsync(l =>
            l.FromType == link.FromType && l.FromId == link.FromId && l.Relation == link.Relation
            && l.ToType == link.ToType && l.ToId == link.ToId);
        if (existing is not null)
        {
            // Said again as observed: an inference has now been seen directly.
            if (existing.Basis == LinkBasis.Inferred && link.Basis == LinkBasis.Observed)
            {
                existing.Basis = LinkBasis.Observed;
                await db.SaveChangesAsync();
            }
            return (existing, false, null);
        }

        link.CreatedAt = DateTime.UtcNow;
        db.Links.Add(link);
        await db.SaveChangesAsync();
        return (link, true, null);
    }

    public async Task<bool> DeleteLinkAsync(Guid id)
    {
        var l = await db.Links.FirstOrDefaultAsync(x => x.Id == id);
        if (l is null) return false;
        db.Links.Remove(l);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<LinkedRecord>> GetLinksAsync(string type, string id)
    {
        id = CanonicalId(type, id);
        var links = await db.Links.AsNoTracking()
            .Where(l => (l.FromType == type && l.FromId == id) || (l.ToType == type && l.ToId == id))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        var result = new List<LinkedRecord>();
        foreach (var l in links)
        {
            var outgoing = l.FromType == type && l.FromId == id;
            var (otherType, otherId) = outgoing ? (l.ToType, l.ToId) : (l.FromType, l.FromId);
            result.Add(new LinkedRecord(l.Id, l.Relation, outgoing ? "out" : "in", l.Basis, l.Source,
                otherType, otherId, await DescribeAsync(otherType, otherId), l.CreatedAt));
        }
        return result;
    }

    // A one-line description of any linkable record; null when it does not exist.
    public async Task<string?> DescribeAsync(string type, string id)
    {
        if (type == RecordTypes.Fact)
            return await db.Facts.AsNoTracking().FirstOrDefaultAsync(f => f.Key == id) is { } f ? $"{f.Key}: {f.Value}" : null;
        if (!Guid.TryParse(id, out var g)) return null;
        return type switch
        {
            RecordTypes.Memory => await db.Memories.AsNoTracking().Where(x => x.Id == g)
                .Select(x => x.Status == MemoryStatus.Active ? x.Content : x.Content + " (" + x.Status + ")").FirstOrDefaultAsync(),
            RecordTypes.Event => await db.Events.AsNoTracking().Where(x => x.Id == g).Select(x => x.Title).FirstOrDefaultAsync(),
            RecordTypes.Knowledge => await db.Entries.AsNoTracking().Where(x => x.Id == g).Select(x => x.Summary).FirstOrDefaultAsync(),
            RecordTypes.Action => await db.Actions.AsNoTracking().Where(x => x.Id == g).Select(x => x.Title).FirstOrDefaultAsync(),
            RecordTypes.Entity => await db.Entities.AsNoTracking().Where(x => x.Id == g).Select(x => x.Name).FirstOrDefaultAsync(),
            _ => null,
        };
    }

    // A memory "mentions" each entity it is saved with. Best-effort by design: a name
    // that cannot become an entity never costs the memory itself.
    private async Task LinkEntitiesAsync(MemoryEntry memory, IReadOnlyList<string>? names, string source)
    {
        if (names is null) return;
        foreach (var raw in names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var (type, name) = ParseEntityName(raw);
            var (entity, _, error) = await UpsertEntityAsync(name, type);
            if (entity is null || error is not null) continue;
            await AddLinkAsync(new RecordLink
            {
                FromType = RecordTypes.Memory, FromId = IdOf(memory.Id),
                Relation = LinkRelations.Mentions,
                ToType = RecordTypes.Entity, ToId = IdOf(entity.Id),
                Basis = LinkBasis.Observed, Source = source, SourceRef = memory.SourceRef,
            });
        }
    }

    private static string CanonicalId(string type, string id) =>
        type != RecordTypes.Fact && Guid.TryParse(id, out var g) ? IdOf(g) : id.Trim();

    private static string MergeAliases(NamedEntity e, string? more)
    {
        var nameKey = MemorySearch.Normalise(e.Name);
        var all = e.Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat((more ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.Join(", ", all
            .Where(a => MemorySearch.Normalise(a).Length > 0 && MemorySearch.Normalise(a) != nameKey)
            .DistinctBy(MemorySearch.Normalise));
    }
}
