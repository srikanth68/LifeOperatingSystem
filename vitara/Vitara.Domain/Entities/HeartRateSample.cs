namespace Vitara.Domain.Entities;

public class HeartRateSample
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int Bpm { get; set; }
    public string? Source { get; set; }  // awake | rest | sleep | workout | etc.
}
