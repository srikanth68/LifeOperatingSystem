using System.Text.Json;

namespace San.Application;

// A transaction San believes belongs to one of the user's properties.
public record ExpenseProposal(string TransactionId, string PropertyId, string Category, string? Reason);

// Reading and grounding San's daily "which of these are property expenses?" pass.
//
// The model is given a list of real transactions and a list of real properties and
// asked only to pair them up. It is not asked for amounts, dates or ids it has not
// been shown, because everything it invents here would land in the user's tax records.
//
// So nothing the model returns is trusted on its face. A proposal naming a transaction
// that was not in the batch, or a property that does not exist, is dropped rather than
// corrected -- the same rule InsightGrounding applies to figures, for the same reason.
// The amount and date are never taken from the reply at all; the caller reads them off
// the real transaction the id points to.
public static class PropertyExpenses
{
    // Categories a property expense can fall into. Anything else the model invents is
    // folded to "other" rather than rejected -- getting the property right and the
    // category vague is still useful, and the user fixes the category on confirm.
    private static readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase)
        { "repair", "improvement", "supplies", "utility", "insurance", "tax", "hoa", "mortgage", "rent", "other" };

    public static List<ExpenseProposal> Parse(string? reply)
    {
        var json = FindingParser.ExtractJson((reply ?? "").Trim());
        if (json is null) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);

            // Accept either a bare array or an object wrapping one. Small models drift
            // between the two across runs, and rejecting a good answer over its
            // envelope would be a silent loss of work.
            var array = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement,
                JsonValueKind.Object when doc.RootElement.TryGetProperty("expenses", out var e)
                    && e.ValueKind == JsonValueKind.Array => e,
                JsonValueKind.Object when doc.RootElement.TryGetProperty("proposals", out var p)
                    && p.ValueKind == JsonValueKind.Array => p,
                _ => default,
            };
            if (array.ValueKind != JsonValueKind.Array) return [];

            var list = new List<ExpenseProposal>();
            foreach (var el in array.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;

                var tx = Str(el, "transactionId");
                var property = Str(el, "propertyId");
                if (string.IsNullOrWhiteSpace(tx) || string.IsNullOrWhiteSpace(property)) continue;

                var category = Str(el, "category");
                list.Add(new ExpenseProposal(
                    tx!.Trim(), property!.Trim(),
                    category is not null && Categories.Contains(category) ? category.ToLowerInvariant() : "other",
                    Str(el, "reason")));
            }
            return list;
        }
        catch (JsonException) { return []; }
    }

    // Drops anything that does not refer to something real, and anything claimed twice.
    public static List<ExpenseProposal> Grounded(
        IEnumerable<ExpenseProposal> proposals, ISet<string> realTransactionIds, ISet<string> realPropertyIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<ExpenseProposal>();

        foreach (var p in proposals)
        {
            if (!realTransactionIds.Contains(p.TransactionId)) continue;   // invented, or from another batch
            if (!realPropertyIds.Contains(p.PropertyId)) continue;         // invented property
            // One transaction, one proposal. A model that lists the same charge against
            // two properties has not split it -- it has guessed twice, and a split is a
            // deliberate act the user performs by hand.
            if (!seen.Add(p.TransactionId)) continue;
            kept.Add(p);
        }

        return kept;
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
