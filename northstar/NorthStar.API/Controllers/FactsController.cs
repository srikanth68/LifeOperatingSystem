using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/facts")]
public class FactsController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() =>
        Ok(await repo.GetAllFactsAsync());

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        var f = await repo.GetFactAsync(key);
        return f is not null ? Ok(f) : NotFound();
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(string key, [FromBody] FactRequest req)
    {
        await repo.UpsertFactAsync(new UserFact
        {
            Key = key,
            Value = req.Value,
            Source = req.Source ?? "manual",
        });
        return Ok(new { key, req.Value });
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key) =>
        await repo.DeleteFactAsync(key) ? NoContent() : NotFound();
}

public record FactRequest(string Value, string? Source);
