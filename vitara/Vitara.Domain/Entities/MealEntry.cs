namespace Vitara.Domain.Entities;

public class MealEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Day { get; set; }
    public string MealType { get; set; } = "snack";
    public string FoodName { get; set; } = "";
    public int? FdcId { get; set; }
    public double ServingQty { get; set; } = 1;
    public string? ServingUnit { get; set; }
    public double Calories { get; set; }
    public double Protein { get; set; }
    public double Carbs { get; set; }
    public double Fat { get; set; }
    public double? Fiber { get; set; }
    // Where this row came from. Without it a sync cannot replace its own entries
    // without also deleting food logged by hand -- and MyFitnessPal entries are edited
    // and deleted after the fact often enough that replacing the day is the only
    // correct way to mirror it.
    public string Source { get; set; } = "manual";   // manual | mfp

    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
}
