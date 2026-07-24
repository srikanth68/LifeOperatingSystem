using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/weighins")]
public class WeighInsController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 180)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await repo.GetWeighInsAsync(to.AddDays(-days), to));
    }

    [HttpPost]
    public async Task<IActionResult> Log([FromBody] WeighInRequest req)
    {
        if (req.WeightKg <= 0) return BadRequest("Weight must be positive.");
        var day = string.IsNullOrWhiteSpace(req.Day)
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : DateOnly.Parse(req.Day);

        var weighIn = new WeighIn
        {
            Id = day.ToString("yyyy-MM-dd"),
            Day = day,
            WeightKg = req.WeightKg,
            CreatedAt = DateTime.UtcNow,
        };
        await repo.UpsertWeighInAsync(weighIn);
        return Ok(weighIn);
    }
}

public record WeighInRequest(string? Day, double WeightKg);
