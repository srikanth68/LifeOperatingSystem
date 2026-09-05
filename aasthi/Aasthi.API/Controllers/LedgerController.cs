using Microsoft.AspNetCore.Mvc;
using Aasthi.Application;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

// Attaching real bank transactions to properties.
//
// Two ways in, deliberately different:
//
//   MANUAL   the user assigns a transaction themselves. Confirmed on arrival, silent,
//            no review step -- they already decided, and asking them to confirm their
//            own action twice is the kind of friction that stops people using a thing.
//
//   PROPOSED the daily pass finds a transaction it believes belongs to a property and
//            writes it as pending. Nothing enters the books until the user says so.
//
// The asymmetry is the point. An unclassified expense costs a minute at tax time; a
// wrongly categorised capital improvement is a wrong answer to an auditor three years
// later, and by then the receipt is gone. So the model proposes and the user disposes.
[ApiController, Route("api/ledger")]
public class LedgerController(IAasthiRepository repo, IVaultTransactions vault, ILogger<LedgerController> logger) : ControllerBase
{
    // Assign a bank transaction to a property. This is the "edit the transaction" path
    // from Vault -- the user picks a property, and the link is written here rather than
    // as a column on Vault's transaction, so a Plaid resync cannot drop it.
    [HttpPost("assign")]
    public async Task<IActionResult> Assign([FromBody] AssignRequest req)
    {
        if (req.PropertyId == Guid.Empty) return BadRequest(new { error = "propertyId is required." });
        if (string.IsNullOrWhiteSpace(req.VaultTransactionId))
            return BadRequest(new { error = "vaultTransactionId is required." });
        if (await repo.GetPropertyAsync(req.PropertyId) is null)
            return NotFound(new { error = "No such property." });
        if (!DateOnly.TryParse(req.Date, out var date))
            return BadRequest(new { error = "date must be YYYY-MM-DD." });

        // Several entries may point at one transaction on purpose: a single hardware
        // store run can be split across two properties, and that is only expressible
        // if the link lives on the entry rather than on the transaction.
        var entry = await repo.AddFinancialAsync(new PropertyFinancialEntry
        {
            PropertyId = req.PropertyId,
            Type = req.Type ?? "expense",
            Category = req.Category ?? "other",
            Amount = req.Amount,
            Date = date,
            Notes = req.Notes,
            VaultTransactionId = req.VaultTransactionId,
            Origin = "manual",
            Status = "confirmed",
            ConfirmedAt = DateTime.UtcNow,
            TaxTreatment = req.TaxTreatment ?? "unclassified",
        });

        logger.LogInformation("Manually assigned transaction {Tx} to property {Property}: {Amount} {Category}.",
            req.VaultTransactionId, req.PropertyId, req.Amount, entry.Category);

        return Ok(ToResult(entry));
    }

    // Everything already attached to a transaction, so the UI can show "this one is
    // already split $200 Scoter / $140 Langer" instead of silently creating a third.
    [HttpGet("by-transaction/{vaultTransactionId}")]
    public async Task<IActionResult> ByTransaction(string vaultTransactionId)
    {
        var all = await repo.GetFinancialsAsync();
        return Ok(all.Where(f => f.VaultTransactionId == vaultTransactionId).Select(ToResult));
    }

    // What San proposed and the user has not yet ruled on.
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
        => Ok((await repo.GetFinancialsAsync(status: "pending")).Select(ToResult));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] ConfirmRequest? req)
    {
        var entry = await repo.GetFinancialAsync(id);
        if (entry is null) return NotFound();

        // Confirming is also the moment to correct: San may have the property right and
        // the tax treatment wrong, and making the user reject and re-enter it would be
        // worse than letting them fix it in place.
        if (req?.PropertyId is { } pid && pid != Guid.Empty) entry.PropertyId = pid;
        if (!string.IsNullOrWhiteSpace(req?.Category)) entry.Category = req!.Category!;
        if (!string.IsNullOrWhiteSpace(req?.TaxTreatment)) entry.TaxTreatment = req!.TaxTreatment!;
        if (req?.Amount is { } amt) entry.Amount = amt;

        entry.Status = "confirmed";
        entry.ConfirmedAt = DateTime.UtcNow;
        await repo.UpdateFinancialAsync(entry);

        logger.LogInformation("Confirmed proposed entry {Id} ({Amount} {Category}).", id, entry.Amount, entry.Category);
        return Ok(ToResult(entry));
    }

    // Kept, not deleted. The row is the tombstone that stops tomorrow's pass proposing
    // the same transaction again -- which is precisely how the reminders turned into
    // something to be ignored.
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id)
    {
        var entry = await repo.GetFinancialAsync(id);
        if (entry is null) return NotFound();

        entry.Status = "rejected";
        await repo.UpdateFinancialAsync(entry);
        return Ok(new { id, status = entry.Status });
    }

    // Match outstanding recurring charges against real bank transactions.
    //
    // One Vault fetch for the whole window rather than one per occurrence: a year of
    // monthly charges across four properties is fifty round trips done the naive way,
    // and the matcher works just as well over a superset.
    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(
        [FromQuery] string? from = null, [FromQuery] string? to = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = DateOnly.TryParse(from, out var f) ? f : today.AddDays(-90);
        var end = DateOnly.TryParse(to, out var t) ? t : today;

        var charges = await repo.GetRecurringChargesAsync(activeOnly: true);
        if (charges.Count == 0) return Ok(new { matched = 0, pending = 0, note = "No active recurring charges." });

        // Widened either side so a payment made a few days outside the window still
        // counts for an occurrence inside it.
        var transactions = await vault.GetAsync(start.AddDays(-14), end.AddDays(14), ct: ct);
        if (transactions.Count == 0)
            return Ok(new { matched = 0, pending = 0, note = "No transactions returned by Vault for this window." });

        var linked = await repo.GetLinkedTransactionIdsAsync(transactions.Select(x => x.Id));
        var existing = await repo.GetFinancialsAsync();
        var matched = 0;
        var pending = 0;

        foreach (var charge in charges)
        {
            foreach (var due in charge.OccurrencesBetween(start, end))
            {
                // Already accounted for -- by a previous run, or by hand.
                if (existing.Any(e => e.RecurringChargeId == charge.Id
                                   && Math.Abs(e.Date.DayNumber - due.DayNumber) <= 15)) continue;

                // A transaction may only ever settle one obligation. Without this the
                // same rent deposit could close out two months.
                var available = transactions.Where(x => !linked.Contains(x.Id));
                var hit = TransactionMatcher.Best(charge, due, available);
                if (hit is null) continue;

                var confident = hit.Value.Confidence >= TransactionMatcher.AutoConfirmThreshold;
                var entry = await repo.AddFinancialAsync(new PropertyFinancialEntry
                {
                    PropertyId = charge.PropertyId,
                    Type = charge.Direction,
                    Category = charge.Category,
                    Amount = hit.Value.Transaction.Magnitude,
                    Date = hit.Value.Transaction.Date,
                    Notes = hit.Value.Transaction.Description,
                    VaultTransactionId = hit.Value.Transaction.Id,
                    RecurringChargeId = charge.Id,
                    MatchConfidence = hit.Value.Confidence,
                    Origin = "recurring",
                    Status = confident ? "confirmed" : "pending",
                    ConfirmedAt = confident ? DateTime.UtcNow : null,
                    // Recurring money is classified by what it IS, not by reading a
                    // receipt: rent is income, mortgage interest and HOA are deductible.
                    // One-off spending stays unclassified because only a human can tell
                    // a repair from an improvement.
                    TaxTreatment = charge.Direction == "income" ? "unclassified" : "deductible",
                });

                linked.Add(hit.Value.Transaction.Id);
                existing.Add(entry);
                if (confident) matched++; else pending++;
            }
        }

        logger.LogInformation("Reconcile {From}..{To}: {Matched} confirmed, {Pending} awaiting review.",
            start, end, matched, pending);
        return Ok(new { matched, pending, from = start.ToString("yyyy-MM-dd"), to = end.ToString("yyyy-MM-dd") });
    }

    // Bank transactions nothing has claimed yet.
    //
    // Everything unlinked is returned rather than a pre-filtered guess at what looks
    // property-related. There is no rule in C# that can tell a hardware-store run for
    // a rental from one for the user's own kitchen -- that judgement needs to know
    // whose life this is, which is exactly the part San is for. Vault syncs once a day
    // and a day is a handful of rows, so there is nothing to save by guessing here.
    [HttpGet("unassigned")]
    public async Task<IActionResult> Unassigned([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-Math.Clamp(days, 1, 120));

        var transactions = await vault.GetAsync(from, to, ct: ct);
        if (transactions.Count == 0) return Ok(Array.Empty<object>());

        // Linked covers all three ways a transaction leaves this list: matched to a
        // recurring charge, assigned by hand, or explicitly rejected. Rejections are
        // the reason tomorrow's pass does not raise the same row again.
        var linked = await repo.GetLinkedTransactionIdsAsync(transactions.Select(x => x.Id));

        return Ok(transactions
            .Where(t => !linked.Contains(t.Id))
            .OrderByDescending(t => t.Date)
            .Select(t => new
            {
                t.Id,
                date = t.Date.ToString("yyyy-MM-dd"),
                // Sign is flipped away here: the caller is a language model being asked
                // "is this a property expense", and "amount 340, outgoing" is far less
                // likely to be misread than Plaid's positive-means-out convention.
                amount = t.Magnitude,
                direction = t.IsMoneyOut ? "out" : "in",
                t.Description,
                t.MerchantName,
            }));
    }

    // San's daily pass proposes a property for a transaction it recognises. Written as
    // pending: nothing San decides enters the books until the user agrees.
    [HttpPost("propose")]
    public async Task<IActionResult> Propose([FromBody] ProposeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.VaultTransactionId))
            return BadRequest(new { error = "vaultTransactionId is required." });
        if (await repo.GetPropertyAsync(req.PropertyId) is null)
            return NotFound(new { error = "No such property." });
        if (!DateOnly.TryParse(req.Date, out var date))
            return BadRequest(new { error = "date must be YYYY-MM-DD." });

        // Idempotent by transaction. A worker that runs twice, or a model that names
        // the same transaction twice in one reply, must not produce two proposals --
        // and it must never overwrite a decision the user has already made by hand.
        var already = await repo.GetLinkedTransactionIdsAsync([req.VaultTransactionId]);
        if (already.Count > 0)
            return Conflict(new { error = "That transaction is already assigned, proposed or rejected." });

        var entry = await repo.AddFinancialAsync(new PropertyFinancialEntry
        {
            PropertyId = req.PropertyId,
            Type = req.Type ?? "expense",
            Category = req.Category ?? "other",
            Amount = req.Amount,
            Date = date,
            Notes = req.Reason,
            VaultTransactionId = req.VaultTransactionId,
            Origin = "san",
            Status = "pending",
            // Left for the human. A repair and a capital improvement look identical in
            // a bank description, and the difference is depreciation -- San guessing it
            // would be a confident answer to a question it cannot actually see.
            TaxTreatment = "unclassified",
        });

        logger.LogInformation("San proposed transaction {Tx} for property {Property} ({Category}).",
            req.VaultTransactionId, req.PropertyId, entry.Category);
        return Ok(ToResult(entry));
    }

    private static object ToResult(PropertyFinancialEntry e) => new
    {
        e.Id,
        e.PropertyId,
        e.Type,
        e.Category,
        e.Amount,
        date = e.Date.ToString("yyyy-MM-dd"),
        e.Notes,
        e.VaultTransactionId,
        e.Origin,
        e.Status,
        e.MatchConfidence,
        e.TaxTreatment,
        e.ReceiptDocumentId,
        e.RecurringChargeId,
        e.ConfirmedAt,
        e.CreatedAt,
    };
}

public record AssignRequest(
    string VaultTransactionId, Guid PropertyId, decimal Amount, string Date,
    string? Type, string? Category, string? TaxTreatment, string? Notes);

public record ConfirmRequest(Guid? PropertyId, string? Category, string? TaxTreatment, decimal? Amount);

public record ProposeRequest(
    string VaultTransactionId, Guid PropertyId, decimal Amount, string Date,
    string? Type, string? Category, string? Reason);
