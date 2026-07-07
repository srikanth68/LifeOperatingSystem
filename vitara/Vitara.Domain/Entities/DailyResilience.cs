namespace Vitara.Domain.Entities;

public class DailyResilience
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public string? Level { get; set; }        // limited | adequate | solid | strong | exceptional
    public int? SleepRecovery { get; set; }   // 0-100
    public int? DaytimeRecovery { get; set; } // 0-100
    public int? Stress { get; set; }          // 0-100
}
