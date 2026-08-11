using Aasthi.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Aasthi.API.Controllers;

// One text search across everything Aasthi holds, so a question like "what did the
// HVAC work cost" does not require knowing in advance whether the answer lives in a
// maintenance log, a task, or a financial entry.
//
// Exists for San more than for the UI: the agent previously had aasthi_properties,
// which lists properties and nothing else, so anything recorded against a property
// was unreachable — it could see that a house existed and nothing that had happened
// to it.
[ApiController, Route("api/search")]
public class SearchController(IAasthiRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest("A search term (q) is required.");

        var term = q.Trim();
        var take = Math.Clamp(limit, 1, 100);

        // Aasthi's datasets are small enough that filtering in memory beats four
        // hand-written queries; revisit if a portfolio ever gets big enough to notice.
        var properties = await repo.GetPropertiesAsync();
        var tasks = await repo.GetTasksAsync();
        var maintenance = await repo.GetMaintenanceAsync();
        var financials = await repo.GetFinancialsAsync();

        var propertyName = properties.ToDictionary(p => p.Id, p => p.Address);
        string Where(Guid id) => propertyName.TryGetValue(id, out var a) ? a : "unknown property";

        var propHits = properties
            .Where(p => Match(term, p.Address, p.City, p.State, p.Zip, p.Notes))
            .Take(take)
            .Select(p => new { type = "property", p.Id, p.Address, p.City, p.State, p.Notes });

        var taskHits = tasks
            .Where(t => Match(term, t.Title, t.Description, t.Status, t.Priority))
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .Select(t => new { type = "task", t.Id, t.Title, t.Description, t.Status, t.Priority, property = Where(t.PropertyId) });

        var maintHits = maintenance
            .Where(m => Match(term, m.Title, m.Description, m.VendorName, m.Category))
            .OrderByDescending(m => m.CompletedDate)
            .Take(take)
            .Select(m => new { type = "maintenance", m.Id, m.Title, m.Description, m.VendorName, m.Cost, m.CompletedDate, property = Where(m.PropertyId) });

        var finHits = financials
            .Where(f => Match(term, f.Notes, f.Category, f.Type))
            .OrderByDescending(f => f.Date)
            .Take(take)
            // `entryType` rather than f.Type verbatim: the result marker below is also
            // called `type`, and two properties serialising to the same JSON name
            // throws at write time rather than at compile time.
            .Select(f => new { type = "financial", f.Id, f.Notes, f.Category, entryType = f.Type, f.Amount, f.Date, property = Where(f.PropertyId) });

        return Ok(new
        {
            query = term,
            properties = propHits,
            tasks = taskHits,
            maintenance = maintHits,
            financials = finHits,
            total = propHits.Count() + taskHits.Count() + maintHits.Count() + finHits.Count(),
        });
    }

    // Case-insensitive contains over any supplied field, skipping nulls so an entity
    // with sparse optional fields still matches on the ones it does have.
    private static bool Match(string term, params string?[] fields) =>
        fields.Any(f => !string.IsNullOrEmpty(f)
                        && f.Contains(term, StringComparison.OrdinalIgnoreCase));
}
