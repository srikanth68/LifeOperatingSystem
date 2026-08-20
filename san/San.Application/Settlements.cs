using System.Text.Json;

namespace San.Application;

// Something the user owed that an email says is now dealt with.
public record Settlement(string Vendor, string? What, decimal? Amount)
{
    // What gets matched against open reminders and actions. Vendor carries the
    // identifying word; "what" adds the kind of obligation when the model supplied it.
    public string Probe => string.Join(" ", new[] { Vendor, What }
        .Where(x => !string.IsNullOrWhiteSpace(x)));
}

// Closing the loop: an email saying a bill is paid should retire the reminder about
// paying it.
//
// Everything needed for this already existed and nothing joined it up. The triage
// prompt only ever described CREATING things, so "Your payment was received" was
// either ignored as a routine notification or, worse, turned into another reminder --
// which is part of why the same bill kept coming back.
//
// The division of labour is deliberate. The model does the one thing only a model can:
// read an email and decide it is a payment confirmation, and for what. The MATCHING is
// arithmetic, and it stays here.
//
// The alternative -- telling the model to call reminders_list, pick the right GUID and
// call reminder_complete -- is a five-step chain in which one wrong GUID silently marks
// the wrong bill paid. A duplicate reminder is annoying; a wrongly-closed one means a
// missed payment. So the model never chooses what to close.
public static class Settlements
{
    // Reads the optional "settled" array from the triage reply. Absent, empty, or
    // malformed all mean the same thing here: nothing was settled. Unlike a finding,
    // there is no useful fallback for a settlement the model garbled -- guessing would
    // close something.
    public static IReadOnlyList<Settlement> Parse(string? reply)
    {
        var json = FindingParser.ExtractJson((reply ?? "").Trim());
        if (json is null) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            if (!doc.RootElement.TryGetProperty("settled", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<Settlement>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var vendor = Str(el, "vendor");
                if (string.IsNullOrWhiteSpace(vendor)) continue;   // nothing to match on

                decimal? amount = el.TryGetProperty("amount", out var a)
                    && a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out var d) ? d : null;

                list.Add(new Settlement(vendor!.Trim(), Str(el, "what"), amount));
            }
            return list;
        }
        catch (JsonException) { return []; }
    }

    // Open items this settlement plainly refers to.
    //
    // Multiple matches are expected rather than suspicious: the whole reason this is
    // needed is that the same bill accumulated several reminders. Closing all of them
    // is the point.
    public static List<T> MatchesIn<T>(Settlement settlement, IEnumerable<T> open, Func<T, string> describe)
        => open.Where(item => DuplicateGuard.NamesTheSameThing(settlement.Probe, describe(item))).ToList();

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
