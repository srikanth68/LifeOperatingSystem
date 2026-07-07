namespace Vitara.Domain.Entities;

public class DailyNutrition
{
    public string Id { get; set; } = "";
    public DateOnly Day { get; set; }
    public int Calories { get; set; }
    public double Protein { get; set; }
    public double Carbs { get; set; }
    public double Fat { get; set; }
    public double? Fiber { get; set; }
    public double? Sugar { get; set; }
    public double? Sodium { get; set; }
    public int? CalorieGoal { get; set; }
    public double? ProteinGoal { get; set; }
    public double? CarbGoal { get; set; }
    public double? FatGoal { get; set; }
    public string? MealsJson { get; set; }
}
