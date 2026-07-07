namespace Vitara.Domain.Entities;

public class Workout
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public string Activity { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? Calories { get; set; }
    public int? Distance { get; set; }       // meters
    public string? Intensity { get; set; }    // easy | moderate | hard
    public string? Label { get; set; }
    public string? Source { get; set; }       // manual | autodetected
}
