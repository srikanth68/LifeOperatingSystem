namespace Vitara.Domain.Entities;

public class DailyCardiovascularAge
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public double? VascularAge { get; set; }
}
