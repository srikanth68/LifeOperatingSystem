using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.Worker.Data;
using Vault.Worker.Services;
using Maaya.Time;

namespace Vault.API.Controllers;

// The things that bill you whether you use them or not.
//
// Worked out from the statement rather than kept as a list, because a list maintained
// by hand is stale the week after it is written and the whole value here is answering
// a question nobody can answer from memory: what do I spend a year on things I forgot
// I bought.
[ApiController, Route("api/recurring")]
public class RecurringController(VaultDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 400)
    {
        // Thirteen months by default, so an annual renewal is seen at least twice and
        // a yearly subscription is detectable at all.
        var today = MaayaClock.Today.ToDateTime(TimeOnly.MinValue);
        var from = today.AddDays(-Math.Clamp(days, 90, 1825));

        var transactions = await db.Transactions
            .Where(t => t.TransactionDate >= from)
            .ToListAsync();

        var found = RecurringCharges.Find(transactions, today);

        return Ok(new
        {
            windowDays = (today - from).Days,
            transactionsExamined = transactions.Count,

            found.Verdict,
            found.MonthlyTotal,
            found.AnnualTotal,

            // The three lists worth acting on, lifted out rather than left for a caller
            // to filter: a price rise eight months ago is invisible on a statement, and
            // a charge that stopped arriving is either a cancellation you made or one
            // you did not.
            priceRises = found.PriceRises.Select(Shape),
            dueSoon = found.DueSoon.Select(Shape),
            lapsed = found.Lapsed.Select(Shape),

            charges = found.Charges.Select(Shape),

            // Said rather than implied. Four occurrences at a steady interval is the
            // bar, so a subscription started three months ago is not in here and its
            // absence is not evidence it does not exist.
            method = "A merchant is counted as recurring after four charges at a steady interval with " +
                     "steady amounts. Anything newer, or anything that varies a lot month to month, is not " +
                     "listed — absence here is not proof a subscription does not exist.",
        });
    }

    private static object Shape(RecurringCharges.Charge c) => new
    {
        c.Merchant,
        c.Amount,
        c.Cadence,
        c.Occurrences,
        c.Category,
        c.Status,
        c.Note,
        lastCharged = c.LastCharged.ToString("yyyy-MM-dd"),
        nextExpected = c.NextExpected.ToString("yyyy-MM-dd"),
        annualCost = c.AnnualCost,
        previousAmount = c.PreviousAmount,
        priceChangedOn = c.PriceChangedOn?.ToString("yyyy-MM-dd"),
        increase = c.PreviousAmount is { } p ? Math.Round(c.Amount - p, 2) : (decimal?)null,
    };
}
