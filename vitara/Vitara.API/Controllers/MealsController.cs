using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/meals")]
public class MealsController(IVitaraRepository repo) : ControllerBase
{
    private static readonly Dictionary<string, double> UnitToGrams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g"] = 1,
        ["gram"] = 1,
        ["grams"] = 1,
        ["oz"] = 28.3495,
        ["ounce"] = 28.3495,
        ["ounces"] = 28.3495,
        ["cup"] = 240,
        ["cups"] = 240,
        ["tbsp"] = 15,
        ["tablespoon"] = 15,
        ["tsp"] = 5,
        ["teaspoon"] = 5,
        ["ml"] = 1,
        ["kg"] = 1000,
        ["lb"] = 453.592,
        ["lbs"] = 453.592,
        ["piece"] = 0,
        ["serving"] = 0,
    };

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? day)
    {
        var d = string.IsNullOrWhiteSpace(day)
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : DateOnly.Parse(day);
        var meals = await repo.GetMealsAsync(d);

        var grouped = meals.GroupBy(m => m.MealType).ToDictionary(
            g => g.Key,
            g => g.Select(m => new
            {
                m.Id, m.FoodName, m.FdcId, m.ServingQty, m.ServingUnit,
                m.Calories, m.Protein, m.Carbs, m.Fat, m.Fiber, m.LoggedAt
            }).ToList() as object);

        var totals = new
        {
            calories = Math.Round(meals.Sum(m => m.Calories), 0),
            protein = Math.Round(meals.Sum(m => m.Protein), 1),
            carbs = Math.Round(meals.Sum(m => m.Carbs), 1),
            fat = Math.Round(meals.Sum(m => m.Fat), 1),
            fiber = meals.Any(m => m.Fiber.HasValue) ? Math.Round(meals.Where(m => m.Fiber.HasValue).Sum(m => m.Fiber!.Value), 1) : (double?)null,
        };

        return Ok(new { day = d.ToString("yyyy-MM-dd"), totals, meals = grouped });
    }

    [HttpPost]
    public async Task<IActionResult> Log([FromBody] LogMealRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FoodName)) return BadRequest("Food name required.");

        var (cal, prot, carbs, fat, fiber) = ComputeNutrients(req);

        var meal = new MealEntry
        {
            Day = string.IsNullOrWhiteSpace(req.Day) ? DateOnly.FromDateTime(DateTime.UtcNow) : DateOnly.Parse(req.Day),
            MealType = req.MealType ?? "snack",
            FoodName = req.FoodName.Trim(),
            FdcId = req.FdcId,
            ServingQty = req.Qty ?? 1,
            ServingUnit = req.Unit ?? "serving",
            Calories = cal,
            Protein = prot,
            Carbs = carbs,
            Fat = fat,
            Fiber = fiber,
        };

        return Ok(await repo.AddMealAsync(meal));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LogMealRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FoodName)) return BadRequest("Food name required.");

        var (cal, prot, carbs, fat, fiber) = ComputeNutrients(req);

        var meal = new MealEntry
        {
            Id = id,
            MealType = req.MealType ?? "snack",
            FoodName = req.FoodName.Trim(),
            ServingQty = req.Qty ?? 1,
            ServingUnit = req.Unit ?? "serving",
            Calories = cal,
            Protein = prot,
            Carbs = carbs,
            Fat = fat,
            Fiber = fiber,
        };

        var updated = await repo.UpdateMealAsync(meal);
        return updated is not null ? Ok(updated) : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteMealAsync(id) ? NoContent() : NotFound();

    private static (double cal, double prot, double carbs, double fat, double? fiber) ComputeNutrients(LogMealRequest req)
    {
        var qty = req.Qty ?? 1;
        var unit = req.Unit ?? "serving";

        double grams;
        if (UnitToGrams.TryGetValue(unit, out var factor) && factor > 0)
        {
            grams = qty * factor;
        }
        else if (req.ServingSizeG is > 0)
        {
            grams = qty * req.ServingSizeG.Value;
        }
        else
        {
            grams = qty * 100;
        }

        var scale = grams / 100.0;

        return (
            Math.Round((req.CalPer100 ?? 0) * scale, 0),
            Math.Round((req.ProtPer100 ?? 0) * scale, 1),
            Math.Round((req.CarbsPer100 ?? 0) * scale, 1),
            Math.Round((req.FatPer100 ?? 0) * scale, 1),
            req.FiberPer100 is not null ? Math.Round(req.FiberPer100.Value * scale, 1) : null
        );
    }
}

public record LogMealRequest(
    string? Day, string? MealType, string FoodName, int? FdcId,
    double? Qty, string? Unit,
    double? ServingSizeG,
    double? CalPer100, double? ProtPer100, double? CarbsPer100, double? FatPer100, double? FiberPer100
);
