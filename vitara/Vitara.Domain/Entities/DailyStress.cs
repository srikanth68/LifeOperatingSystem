namespace Vitara.Domain.Entities;

public class DailyStress
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? StressHighSeconds { get; set; }
    public int? RecoveryHighSeconds { get; set; }
    public string? DaySummary { get; set; }    // restored | normal | strained
}
