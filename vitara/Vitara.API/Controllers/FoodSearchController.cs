using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Vitara.API.Controllers;

[ApiController, Route("api/food")]
public class FoodSearchController(IHttpClientFactory httpFactory) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int pageSize = 10)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest("Query required.");

        var apiKey = Environment.GetEnvironmentVariable("USDA_API_KEY") ?? "DEMO_KEY";
        var client = httpFactory.CreateClient();
        var url = $"https://api.nal.usda.gov/fdc/v1/foods/search?query={Uri.EscapeDataString(q)}&pageSize={pageSize}&api_key={apiKey}";

        var resp = await client.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, "USDA API error");

        var body = await resp.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<UsdaSearchResult>(body, JsonOpts);

        var foods = result?.Foods?.Select(f => new
        {
            fdcId = f.FdcId,
            name = f.Description,
            brand = f.BrandName ?? f.BrandOwner,
            nutrients = ExtractNutrients(f.FoodNutrients),
            servingSize = f.ServingSize,
            servingUnit = f.ServingSizeUnit,
        }) ?? [];

        return Ok(foods);
    }

    [HttpGet("{fdcId:int}")]
    public async Task<IActionResult> GetFood(int fdcId)
    {
        var apiKey = Environment.GetEnvironmentVariable("USDA_API_KEY") ?? "DEMO_KEY";
        var client = httpFactory.CreateClient();
        var url = $"https://api.nal.usda.gov/fdc/v1/food/{fdcId}?api_key={apiKey}";

        var resp = await client.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    private static NutrientInfo ExtractNutrients(List<UsdaNutrient>? nutrients)
    {
        if (nutrients is null) return new NutrientInfo();
        double? Get(int id) => nutrients.FirstOrDefault(n => n.NutrientId == id)?.Value;
        return new NutrientInfo
        {
            Calories = Get(1008),
            Protein = Get(1003),
            Carbs = Get(1005),
            Fat = Get(1004),
            Fiber = Get(1079),
            Sugar = Get(2000),
            Sodium = Get(1093),
        };
    }
}

public class NutrientInfo
{
    public double? Calories { get; set; }
    public double? Protein { get; set; }
    public double? Carbs { get; set; }
    public double? Fat { get; set; }
    public double? Fiber { get; set; }
    public double? Sugar { get; set; }
    public double? Sodium { get; set; }
}

public class UsdaSearchResult { public List<UsdaFood>? Foods { get; set; } }
public class UsdaFood
{
    public int FdcId { get; set; }
    public string Description { get; set; } = "";
    public string? BrandName { get; set; }
    public string? BrandOwner { get; set; }
    public double? ServingSize { get; set; }
    public string? ServingSizeUnit { get; set; }
    public List<UsdaNutrient>? FoodNutrients { get; set; }
}
public class UsdaNutrient
{
    public int NutrientId { get; set; }
    public string? NutrientName { get; set; }
    public double? Value { get; set; }
    public string? UnitName { get; set; }
}
