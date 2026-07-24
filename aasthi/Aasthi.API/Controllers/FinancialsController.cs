using Microsoft.AspNetCore.Mvc;
using Aasthi.Application.DTOs;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

[ApiController, Route("api")]
public class FinancialsController(IAasthiRepository repo) : ControllerBase
{
    [HttpGet("properties/{propertyId:guid}/financials")]
    public async Task<IActionResult> List(Guid propertyId)
    {
        var entries = await repo.GetFinancialsAsync(propertyId);
        return Ok(entries.Select(ToResult));
    }

    [HttpPost("properties/{propertyId:guid}/financials")]
    public async Task<IActionResult> Create(Guid propertyId, [FromBody] FinancialEntryRequest req)
    {
        if (req.Amount <= 0) return BadRequest("Amount must be positive.");
        var entry = new PropertyFinancialEntry
        {
            PropertyId = propertyId,
            Type       = req.Type,
            Category   = req.Category,
            Amount     = req.Amount,
            Date       = req.Date,
            Notes      = req.Notes,
        };
        var created = await repo.AddFinancialAsync(entry);
        return Created($"/api/financials/{created.Id}", ToResult(created));
    }

    [HttpDelete("financials/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteFinancialAsync(id) ? NoContent() : NotFound();

    [HttpGet("financials/summary")]
    public async Task<IActionResult> Summary()
    {
        var properties = await repo.GetPropertiesAsync();
        var entries = await repo.GetFinancialsAsync();
        var byProp = entries.GroupBy(e => e.PropertyId).ToDictionary(g => g.Key, g => g.ToList());

        var perProperty = new List<PropertyCashFlow>();
        foreach (var p in properties)
        {
            var list = byProp.TryGetValue(p.Id, out var l) ? l : [];
            var income   = list.Where(e => e.Type == "income").Sum(e => e.Amount);
            var expenses = list.Where(e => e.Type == "expense").Sum(e => e.Amount);
            var mortgage = list.Where(e => e.Type == "mortgage").Sum(e => e.Amount);
            var net = income - expenses - mortgage;

            // Annualize the observed cash flow over the logged date span, expressed as % of purchase price.
            double? cashOnCash = null;
            if (p.PurchasePrice > 0 && list.Count > 0)
            {
                var minDate = list.Min(e => e.Date);
                var maxDate = list.Max(e => e.Date);
                var days = Math.Max(1, maxDate.DayNumber - minDate.DayNumber + 1);
                var annualized = net * 365m / days;
                cashOnCash = (double)(annualized / p.PurchasePrice * 100);
            }

            perProperty.Add(new PropertyCashFlow(
                p.Id, p.Address, income, expenses, mortgage, net,
                p.ProfitAmount, p.ProfitPct, cashOnCash));
        }

        return Ok(new FinancialsSummary(
            entries.Where(e => e.Type == "income").Sum(e => e.Amount),
            entries.Where(e => e.Type == "expense").Sum(e => e.Amount),
            entries.Where(e => e.Type == "mortgage").Sum(e => e.Amount),
            entries.Where(e => e.Type == "income").Sum(e => e.Amount)
                - entries.Where(e => e.Type == "expense").Sum(e => e.Amount)
                - entries.Where(e => e.Type == "mortgage").Sum(e => e.Amount),
            perProperty));
    }

    private static FinancialEntryResult ToResult(PropertyFinancialEntry e) => new(
        e.Id, e.PropertyId, e.Type, e.Category, e.Amount, e.Date, e.Notes, e.CreatedAt);
}
