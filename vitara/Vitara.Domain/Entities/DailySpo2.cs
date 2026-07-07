namespace Vitara.Domain.Entities;

public class DailySpo2
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public double? Spo2Average { get; set; }          // %
    public double? BreathingDisturbanceIndex { get; set; }
}
