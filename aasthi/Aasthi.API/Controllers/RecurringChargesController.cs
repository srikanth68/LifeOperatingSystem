using Microsoft.AspNetCore.Mvc;
using Aasthi.Application;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

// The money that is supposed to move: rent in, mortgage and HOA out.
//
// Aasthi could always record what happened. It could never say what was SUPPOSED to
// happen, so a rent payment that never arrived looked exactly like a month nobody had
// got round to entering. These are the rules that make an absence visible.
[ApiController, Route("api/recurring-charges")]
public class RecurringChargesController(IAasthiRepository repo, IVaultTransactions vault, ILogger<RecurringChargesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? propertyId = null, [FromQuery] bool activeOnly = false)
        => Ok((await repo.GetRecurringChargesAsync(propertyId, activeOnly)).Select(ToResult));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RecurringChargeRequest req)
    {
        if (await repo.GetPropertyAsync(req.PropertyId) is null)
            return NotFound(new { error = "No such property." });
        if (!DateOnly.TryParse(req.StartDate, out var start))
            return BadRequest(new { error = "startDate must be YYYY-MM-DD." });
        if (req.Amount <= 0) return BadRequest(new { error = "amount must be positive." });

        DateOnly? end = DateOnly.TryParse(req.EndDate, out var e) ? e : null;

        var charge = await repo.AddRecurringChargeAsync(new RecurringCharge
        {
            PropertyId = req.PropertyId,
            Direction = req.Direction ?? "expense",
            Category = req.Category ?? "other",
            Amount = req.Amount,
            Frequency = req.Frequency ?? "monthly",
            DueDay = req.DueDay ?? 1,
            StartDate = start,
            EndDate = end,
            MatchHint = req.MatchHint,
            Notes = req.Notes,
        });

        logger.LogInformation("Recurring charge added: {Category} {Amount} {Frequency} for property {Property}.",
            charge.Category, charge.Amount, charge.Frequency, charge.PropertyId);
        return Ok(ToResult(charge));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RecurringChargeRequest req)
    {
        var existing = await repo.GetRecurringChargeAsync(id);
        if (existing is null) return NotFound();

        if (!DateOnly.TryParse(req.StartDate, out var start))
            return BadRequest(new { error = "startDate must be YYYY-MM-DD." });

        existing.PropertyId = req.PropertyId;
        existing.Direction = req.Direction ?? existing.Direction;
        existing.Category = req.Category ?? existing.Category;
        existing.Amount = req.Amount;
        existing.Frequency = req.Frequency ?? existing.Frequency;
        existing.DueDay = req.DueDay ?? existing.DueDay;
        existing.StartDate = start;
        existing.EndDate = DateOnly.TryParse(req.EndDate, out var e) ? e : null;
        existing.MatchHint = req.MatchHint;
        existing.Notes = req.Notes;
        existing.Active = req.Active ?? existing.Active;

        await repo.UpdateRecurringChargeAsync(existing);
        return Ok(ToResult(existing));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
        => await repo.DeleteRecurringChargeAsync(id) ? NoContent() : NotFound();

    // Expected against actual, occurrence by occurrence. This is the answer to "how
    // much is rent and did we get it".
    //
    // Nothing here is stored. Each occurrence is derived from the rule and then looked
    // for among real entries, so changing a rent amount does not require rewriting a
    // year of pre-generated rows.
    [HttpGet("status")]
    public async Task<IActionResult> Status(
        [FromQuery] Guid? propertyId = null, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = DateOnly.TryParse(from, out var f) ? f : new DateOnly(today.Year, today.Month, 1);
        var end = DateOnly.TryParse(to, out var t) ? t : start.AddMonths(1).AddDays(-1);

        var charges = await repo.GetRecurringChargesAsync(propertyId, activeOnly: true);
        var entries = await repo.GetFinancialsAsync(propertyId);
        var rows = new List<object>();

        foreach (var c in charges)
        {
            foreach (var due in c.OccurrencesBetween(start, end))
            {
                // Prefer an explicit link. Fall back to a same-category entry near the
                // due date for a sensible amount, which is what a manual entry made
                // before any of this existed will look like.
                var hit = entries.FirstOrDefault(x => x.RecurringChargeId == c.Id && Near(x.Date, due, 15))
                       ?? entries.FirstOrDefault(x =>
                              x.Category == c.Category && x.RecurringChargeId is null &&
                              Near(x.Date, due, 10) && Within(x.Amount, c.Amount, 0.05m));

                rows.Add(new
                {
                    chargeId = c.Id,
                    c.PropertyId,
                    c.Category,
                    c.Direction,
                    dueDate = due.ToString("yyyy-MM-dd"),
                    expected = c.Amount,
                    actual = hit?.Amount,
                    actualDate = hit?.Date.ToString("yyyy-MM-dd"),
                    entryId = hit?.Id,
                    // "missing" only once it is genuinely late. Rent due on the 1st is
                    // not a problem on the 1st, and flagging it that morning is how a
                    // useful signal turns into one that gets ignored.
                    status = hit is not null ? "received" : due > today ? "upcoming" : "missing",
                });
            }
        }

        return Ok(rows.OrderBy(r => ((dynamic)r).dueDate).ToList());
    }

    // Patterns in the bank feed that look like recurring charges but have not been set
    // up yet. Read-only: it proposes, it never creates.
    //
    // Nothing here knows which property a pattern belongs to, and that is the design.
    // Clustering finds "SUNRIDGE HOA, $340, every three months" from arithmetic alone;
    // deciding it is the Langer Street HOA needs knowledge of the user's life, and that
    // is San's job or the user's. Keeping the two apart means a clustering bug can
    // produce a useless suggestion but never a mislabelled tax record.
    [HttpGet("detect")]
    public async Task<IActionResult> Detect([FromQuery] int months = 12, CancellationToken ct = default)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddMonths(-Math.Clamp(months, 3, 36));

        var transactions = await vault.GetAsync(from, to, limit: 2000, ct: ct);
        if (transactions.Count == 0)
            return Ok(new { candidates = Array.Empty<object>(), note = "No transactions returned by Vault." });

        var existing = await repo.GetRecurringChargesAsync();
        var candidates = RecurringChargeDetector.Detect(transactions);

        logger.LogInformation("Detected {Count} recurring patterns across {Txns} transactions ({Months}m).",
            candidates.Count, transactions.Count, months);

        return Ok(new
        {
            scanned = transactions.Count,
            candidates = candidates.Select(c => new
            {
                c.Direction,
                c.Amount,
                c.Frequency,
                c.DueDay,
                c.MatchHint,
                firstSeen = c.FirstSeen.ToString("yyyy-MM-dd"),
                lastSeen = c.LastSeen.ToString("yyyy-MM-dd"),
                c.Occurrences,
                // Above roughly 0.2 the amount is an estimate rather than a contract --
                // a utility bill, not a rent cheque. Worth showing so the user knows
                // which figure they are agreeing to.
                variableAmount = c.AmountVariability > 0.2m,
                c.SampleDescriptions,
                // Already configured, so the UI can grey it out instead of offering a
                // duplicate of something the user set up last month.
                alreadyConfigured = existing.Any(e =>
                    !string.IsNullOrWhiteSpace(e.MatchHint) &&
                    e.MatchHint!.Equals(c.MatchHint, StringComparison.OrdinalIgnoreCase)),
            }),
        });
    }

    private static bool Near(DateOnly a, DateOnly b, int days) => Math.Abs(a.DayNumber - b.DayNumber) <= days;

    private static bool Within(decimal actual, decimal expected, decimal tolerance) =>
        expected != 0 && Math.Abs(actual - expected) / Math.Abs(expected) <= tolerance;

    private static object ToResult(RecurringCharge c) => new
    {
        c.Id,
        c.PropertyId,
        c.Direction,
        c.Category,
        c.Amount,
        c.Frequency,
        c.DueDay,
        startDate = c.StartDate.ToString("yyyy-MM-dd"),
        endDate = c.EndDate?.ToString("yyyy-MM-dd"),
        c.MatchHint,
        c.Active,
        c.Notes,
        c.CreatedAt,
    };
}

public record RecurringChargeRequest(
    Guid PropertyId, decimal Amount, string StartDate,
    string? Direction, string? Category, string? Frequency, int? DueDay,
    string? EndDate, string? MatchHint, string? Notes, bool? Active);
