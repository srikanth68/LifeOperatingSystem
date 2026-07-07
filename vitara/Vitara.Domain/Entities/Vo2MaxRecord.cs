namespace Vitara.Domain.Entities;

public class Vo2MaxRecord
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public double? Vo2Max { get; set; }  // mL/kg/min
}
