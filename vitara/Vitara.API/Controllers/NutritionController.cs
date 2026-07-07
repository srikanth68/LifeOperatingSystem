using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/nutrition")]
public class NutritionController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);
        return Ok(await repo.GetNutritionAsync(from, to));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int days = 7)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-days);
        var data = await repo.GetNutritionAsync(from, to);
        if (data.Count == 0) return Ok(new { count = 0 });

        return Ok(new
        {
            count = data.Count,
            avgCalories = Math.Round(data.Average(n => (double)n.Calories), 0),
            avgProtein = Math.Round(data.Average(n => n.Protein), 1),
            avgCarbs = Math.Round(data.Average(n => n.Carbs), 1),
            avgFat = Math.Round(data.Average(n => n.Fat), 1),
            avgFiber = data.Any(n => n.Fiber.HasValue) ? Math.Round(data.Where(n => n.Fiber.HasValue).Average(n => n.Fiber!.Value), 1) : (double?)null,
            totalCalories = data.Sum(n => n.Calories),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] List<NutritionEntry> entries)
    {
        if (entries.Count == 0) return BadRequest("No entries.");
        var models = entries.Select(e => new DailyNutrition
        {
            Id = e.Day,
            Day = DateOnly.Parse(e.Day),
            Calories = e.Calories,
            Protein = e.Protein,
            Carbs = e.Carbs,
            Fat = e.Fat,
            Fiber = e.Fiber,
            Sugar = e.Sugar,
            Sodium = e.Sodium,
            CalorieGoal = e.CalorieGoal,
            ProteinGoal = e.ProteinGoal,
            CarbGoal = e.CarbGoal,
            FatGoal = e.FatGoal,
            MealsJson = e.MealsJson,
        }).ToList();

        await repo.UpsertNutritionAsync(models);
        return Ok(new { upserted = models.Count });
    }
}

public record NutritionEntry(
    string Day, int Calories, double Protein, double Carbs, double Fat,
    double? Fiber, double? Sugar, double? Sodium,
    int? CalorieGoal, double? ProteinGoal, double? CarbGoal, double? FatGoal,
    string? MealsJson
);
