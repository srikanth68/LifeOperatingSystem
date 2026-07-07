using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.DTOs;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

[ApiController, Route("api/insights")]
public class InsightsController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool all = false, [FromQuery] int limit = 50)
    {
        var insights = await repo.GetInsightsAsync(all, limit);
        return Ok(insights.Select(ToResult));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] InsightCreateRequest req)
    {
        var insight = new Insight
        {
            Title = req.Title,
            Body = req.Body,
            GeneratedBy = "manual"
        };
        await repo.AddInsightAsync(insight);
        return Ok(ToResult(insight));
    }

    [HttpPatch("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id)
    {
        var insight = await repo.DismissInsightAsync(id);
        return insight is null ? NotFound() : Ok(ToResult(insight));
    }

    private static InsightResult ToResult(Insight i) =>
        new(i.Id, i.Title, i.Body, i.GeneratedBy, i.Dismissed, i.CreatedAt);
}
