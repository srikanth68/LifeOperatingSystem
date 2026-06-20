namespace Vitara.Domain.Entities;

public class SleepSession
{
    public string Id { get; set; } = "";        // Oura session ID
    public DateOnly Day { get; set; }
    public DateTime BedtimeStart { get; set; }
    public DateTime BedtimeEnd { get; set; }
    public int TotalSleepMinutes { get; set; }
    public int RemMinutes { get; set; }
    public int DeepMinutes { get; set; }
    public int LightMinutes { get; set; }
    public int AwakeMinutes { get; set; }
    public int? Score { get; set; }             // 0-100 Oura score
    public double? AvgHrv { get; set; }         // ms
    public double? LowestHr { get; set; }       // bpm
    public double? AvgBreathingRate { get; set; }
    public double? AvgSpo2 { get; set; }        // %
    public double? SkinTempDeviation { get; set; } // °C from baseline
    public double Efficiency => TotalSleepMinutes > 0
        ? (double)TotalSleepMinutes / (BedtimeEnd - BedtimeStart).TotalMinutes
        : 0;
}
