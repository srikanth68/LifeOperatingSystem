namespace NorthStar.Domain.Entities;

public class UserFact
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "manual";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
