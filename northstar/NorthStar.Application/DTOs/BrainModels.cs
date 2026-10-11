using NorthStar.Domain.Entities;

namespace NorthStar.Application.DTOs;

// What saving a memory did. Duplicate: it was already known (and nothing new was stored).
// Similar: live memories that read close to it -- the caller's chance to notice it may be
// restating or contradicting something, and supersede instead of adding.
public record MemoryWriteResult(
    MemoryEntry? Memory,
    bool Duplicate,
    IReadOnlyList<MemoryEntry> Similar,
    string? Note,
    string? Error)
{
    public static MemoryWriteResult Fail(string error) => new(null, false, [], null, error);
}

// A memory with its whole correction chain, oldest first, and what it is about.
public record MemoryDetail(MemoryEntry Memory, IReadOnlyList<MemoryEntry> History, IReadOnlyList<NamedEntity> Entities);

// One edge as seen from a record: which way it points, and a one-line description of
// the record at the other end (null when that record no longer exists).
public record LinkedRecord(
    Guid LinkId, string Relation, string Direction, string Basis, string Source,
    string OtherType, string OtherId, string? OtherSummary, DateTime CreatedAt);

public record EntityDetail(NamedEntity Entity, IReadOnlyList<LinkedRecord> Links);
