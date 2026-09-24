namespace Vitara.Domain.Entities;

public class DailyReadiness
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int? Score { get; set; }             // 0-100
    // Also not what its name says. Oura's contributors carry `body_temperature` as a
    // 0-100 score; a deviation in degrees is a fraction, and reading one into an int
    // truncates 0.21 C to nothing. Either way this is not a temperature. The real
    // figure is SleepSession.SkinTempDeviation, in degrees, which is what the Today tab
    // and the illness detector both read.
    public int? TemperatureContributor { get; set; }
    public int? HrvBalance { get; set; }
    public int? RecoveryIndex { get; set; }
    // NOT a heart rate. Oura's readiness `contributors` are all scores out of 100,
    // including this one -- 100 means "your resting heart rate contributed as well as it
    // can to today's readiness", which happens when it is LOW. It was named
    // RestingHeartRate, served as `restingHr`, labelled bpm in the UI and fed to the
    // biological-age model as though it were a pulse, where a perfect 100 read as a
    // tachycardic resting rate and added ten years. The real figure is
    // SleepSession.LowestHr, which is what the whole analysis layer has always used.
    public int? RestingHrContributor { get; set; }
    public int? ActivityBalance { get; set; }
    public int? SleepBalance { get; set; }
    public string? Level { get; set; }          // "optimal" | "good" | "pay_attention"
}
