namespace Vitara.Domain.Entities;

public class DailyReadiness
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? Score { get; set; }             // 0-100
    public int? TemperatureDeviation { get; set; }
    public int? HrvBalance { get; set; }
    public int? RecoveryIndex { get; set; }
    public int? RestingHeartRate { get; set; }  // bpm
    public int? ActivityBalance { get; set; }
    public int? SleepBalance { get; set; }
    public string? Level { get; set; }          // "optimal" | "good" | "pay_attention"
}
