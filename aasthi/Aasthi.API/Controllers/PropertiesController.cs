using Microsoft.AspNetCore.Mvc;
using Aasthi.Application.DTOs;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

[ApiController, Route("api/properties")]
public class PropertiesController(IAasthiRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var properties = await repo.GetPropertiesAsync();
        return Ok(properties.Select(ToResult));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var properties = await repo.GetPropertiesAsync();
        var totalPurchase = properties.Sum(p => p.PurchasePrice);
        var totalCurrent = properties.Sum(p => p.CurrentValue);
        var totalProfit = totalCurrent - totalPurchase;
        double? totalProfitPct = totalPurchase > 0 ? (double)(totalProfit / totalPurchase * 100) : null;

        return Ok(new PortfolioSummary(properties.Count, totalPurchase, totalCurrent, totalProfit, totalProfitPct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOne(Guid id)
    {
        var p = await repo.GetPropertyAsync(id);
        if (p is null) return NotFound();
        return Ok(ToDetailResult(p));
    }

    [HttpPost]
    public async Task<IActionResult> Create(PropertyUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Address)) return BadRequest("Address is required.");

        var property = new Property
        {
            Address = req.Address,
            City = req.City,
            State = req.State,
            Zip = req.Zip,
            Country = req.Country ?? "USA",
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            PurchasePrice = req.PurchasePrice,
            PurchaseDate = req.PurchaseDate,
            CurrentValue = req.CurrentValue,
            CurrentValueAsOf = req.CurrentValueAsOf,
            Notes = req.Notes ?? "",
        };

        var created = await repo.AddPropertyAsync(property);
        return CreatedAtAction(nameof(GetOne), new { id = created.Id }, ToResult(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PropertyUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Address)) return BadRequest("Address is required.");

        var property = new Property
        {
            Id = id,
            Address = req.Address,
            City = req.City,
            State = req.State,
            Zip = req.Zip,
            Country = req.Country ?? "USA",
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            PurchasePrice = req.PurchasePrice,
            PurchaseDate = req.PurchaseDate,
            CurrentValue = req.CurrentValue,
            CurrentValueAsOf = req.CurrentValueAsOf,
            Notes = req.Notes ?? "",
        };

        var ok = await repo.UpdatePropertyAsync(property);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ok = await repo.DeletePropertyAsync(id);
        return ok ? NoContent() : NotFound();
    }

    private static PropertyResult ToResult(Property p) => new(
        p.Id, p.Address, p.City, p.State, p.Zip, p.Country, p.Latitude, p.Longitude,
        p.PurchasePrice, p.PurchaseDate, p.CurrentValue, p.CurrentValueAsOf, p.Notes, p.CreatedAt,
        p.ProfitAmount, p.ProfitPct, p.Contacts.Count, p.Documents.Count);

    private static PropertyDetailResult ToDetailResult(Property p) => new(
        p.Id, p.Address, p.City, p.State, p.Zip, p.Country, p.Latitude, p.Longitude,
        p.PurchasePrice, p.PurchaseDate, p.CurrentValue, p.CurrentValueAsOf, p.Notes, p.CreatedAt,
        p.ProfitAmount, p.ProfitPct,
        p.Contacts.Select(c => new ContactResult(c.Id, c.PropertyId, c.Name, c.Role, c.Phone, c.Email, c.Notes)).ToList(),
        p.Documents.Select(d => new DocumentResult(d.Id, d.PropertyId, d.FileName, d.ContentType, d.SizeBytes, d.Category, d.UploadedAt)).ToList());
}
