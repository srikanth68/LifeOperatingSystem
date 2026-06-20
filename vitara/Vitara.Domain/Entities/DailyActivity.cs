namespace Vitara.Domain.Entities;

public class DailyActivity
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? Score { get; set; }
    public int Steps { get; set; }
    public int ActiveCalories { get; set; }
    public int TotalCalories { get; set; }
    public int EquivalentWalkingDistance { get; set; } // meters
    public int HighActivityMinutes { get; set; }
    public int MediumActivityMinutes { get; set; }
    public int LowActivityMinutes { get; set; }
    public int SedentaryMinutes { get; set; }
    public int RestMinutes { get; set; }
    public double? AvgMet { get; set; }
}
