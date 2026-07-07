namespace NorthStar.Domain.Entities;

public class ModuleSnapshot
{
    public string Module { get; set; } = "";
    public string SummaryJson { get; set; } = "{}";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
