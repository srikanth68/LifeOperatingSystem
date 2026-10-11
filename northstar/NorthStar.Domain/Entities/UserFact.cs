namespace NorthStar.Domain.Entities;

public class UserFact
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "manual";
    public string? SourceRef { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// A value a fact used to have. A fact holds one current value, and setting it used to
// overwrite the old one with no trace -- so "what was my address before?" or "when did
// this change, and who changed it?" had no answer. Every replacement and deletion now
// leaves one of these behind.
public class FactRevision
{
    public long Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "";
    public string? SourceRef { get; set; }
    public DateTime ValidFrom { get; set; }   // when this value was set
    public DateTime ValidTo { get; set; }     // when it stopped being the value
    public string Change { get; set; } = FactChange.Replaced;
}

public static class FactChange
{
    public const string Replaced = "replaced";
    public const string Deleted = "deleted";
}
