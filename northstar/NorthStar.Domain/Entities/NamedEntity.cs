namespace NorthStar.Domain.Entities;

// A person, place, project, vehicle... that records can be about. Its aliases are what
// let recall bridge words a keyword index cannot: with "my car" as an alias of
// "Honda Civic", asking about the car finds the memories that only ever say Honda.
public class NamedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = EntityTypes.Other;
    public string Name { get; set; } = "";
    // Normalised name; unique, so one thing is one entity however it is capitalised.
    public string NameKey { get; set; } = "";
    public string Aliases { get; set; } = "";   // comma-separated
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public IEnumerable<string> AllNames() =>
        new[] { Name }.Concat(Aliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

public static class EntityTypes
{
    public const string Other = "other";
    public static readonly string[] All =
        ["person", "org", "place", "project", "property", "vehicle", "account", "topic", Other];

    public static bool IsValid(string? type) => type is not null && All.Contains(type);
}

// A typed edge between two records: memory, fact, event, knowledge entry, action or
// entity. "observed" means a record says so directly (a memory that names the Honda);
// "inferred" means someone concluded it -- and must never be shown as more than that.
public class RecordLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FromType { get; set; } = "";
    public string FromId { get; set; } = "";
    public string Relation { get; set; } = LinkRelations.Mentions;
    public string ToType { get; set; } = "";
    public string ToId { get; set; } = "";
    public string Basis { get; set; } = LinkBasis.Observed;
    public string Source { get; set; } = "agent";
    public string? SourceRef { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class RecordTypes
{
    public const string Memory = "memory";
    public const string Fact = "fact";
    public const string Event = "event";
    public const string Knowledge = "knowledge";
    public const string Action = "action";
    public const string Entity = "entity";

    public static readonly string[] All = [Memory, Fact, Event, Knowledge, Action, Entity];

    public static bool IsValid(string? type) => type is not null && All.Contains(type);
}

public static class LinkRelations
{
    public const string Mentions = "mentions";
}

public static class LinkBasis
{
    public const string Observed = "observed";
    public const string Inferred = "inferred";

    public static bool IsValid(string? basis) => basis is Observed or Inferred;
}
