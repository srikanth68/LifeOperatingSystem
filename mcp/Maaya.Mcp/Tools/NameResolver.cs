using System.Text.Json;

namespace Maaya.Mcp.Tools;

// Turns what the user called something into the id the API wants.
//
// Several tools used to demand a GUID and tell the model to fetch it first --
// "reminderId* from reminders_list", "actionId* from actions_pending", "Needs the
// GUID - call karma_habits first". Measured against the real catalogue, that pattern
// fails badly: "I did 30 minutes of reading today, log it" produced a check-in zero
// times out of ten, every run coming back asking a clarifying question instead. The
// model will not reliably chain a lookup, and it cannot invent a GUID, so it stalls
// and asks the user for something the user does not have either.
//
// That is exactly the conversation this system started from: asked to close out the
// tree trimming, San replied "I need the property ID for Scoter Street. Can you
// provide that?" -- to a person who has never seen a GUID in their life.
//
// So the chain moves into code, which is the division of labour the rest of the
// system already uses: the model names the thing, deterministic matching resolves it.
// A resolver that cannot hallucinate an id is worth more than a description reminding
// the model to go and look one up.
public static class NameResolver
{
    // The property names things are called across the modules. Reminders carry "text",
    // actions "title", habits and most else "name".
    private static readonly string[] NameFields = ["name", "text", "title", "summary"];

    public static (string? Id, string? Error) Resolve(string needle, string json, string what)
    {
        if (string.IsNullOrWhiteSpace(needle)) return (null, $"A {what} name or id is required.");

        // Already an id: nothing to do, and the previous contract keeps working for
        // any caller that does have one.
        if (Guid.TryParse(needle.Trim(), out var asGuid)) return (asGuid.ToString(), null);

        List<(string Id, string Name)> candidates;
        try { candidates = Extract(json); }
        catch (JsonException ex) { return (null, $"Could not read the {what} list: {ex.Message}"); }

        if (candidates.Count == 0) return (null, $"There are no open {what}s to match against.");

        var term = needle.Trim();

        // Exact first. Only if nothing matches exactly does it widen, so "reading"
        // never loses to "reading before bed" when both exist.
        var hits = candidates
            .Where(c => string.Equals(c.Name, term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (hits.Count == 0)
            hits = candidates.Where(c => Overlaps(c.Name, term)).ToList();

        if (hits.Count == 0)
            return (null, $"Nothing matches \"{term}\". Open {what}s: {Join(candidates.Select(c => c.Name))}.");

        // Guessing between two would tick off the wrong thing -- silently, and
        // annoyingly to undo. The ambiguity goes back to the user, which is the same
        // rule the settlement matcher follows for the same reason.
        if (hits.Count > 1)
            return (null, $"\"{term}\" matches several: {Join(hits.Select(c => c.Name))}. Which one?");

        return (hits[0].Id, null);
    }

    // Substring either way, plus a shared-word fallback so "tree trimming" finds
    // "Arrange Tree trimming at 15128 Scoter Street" -- which is the shape real
    // reminders actually take, and the exact case that started all of this.
    private static bool Overlaps(string name, string term)
    {
        if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        if (term.Contains(name, StringComparison.OrdinalIgnoreCase)) return true;

        var termWords = Words(term);
        if (termWords.Count == 0) return false;

        // Every meaningful word of what the user said has to appear. Requiring ANY
        // single word would let "call the plumber" match "call the dentist".
        var nameWords = Words(name);
        return termWords.All(w => nameWords.Contains(w));
    }

    // Function words carry no identifying weight, and requiring them to appear breaks
    // ordinary phrasing: "the tree trimming at Scoter" failed to match "Arrange Tree
    // trimming at 15128 Scoter Street" purely because "the" is three letters long and
    // survived the length filter.
    private static readonly HashSet<string> Ignored =
    [
        "the", "and", "for", "with", "that", "this", "from", "into", "about",
        "your", "his", "her", "its", "our", "was", "were", "are", "all", "any",
    ];

    private static HashSet<string> Words(string s) =>
        s.ToLowerInvariant()
            .Split([' ', '\t', '-', '_', '/', ',', '.', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !Ignored.Contains(w))
            .ToHashSet();

    private static List<(string Id, string Name)> Extract(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Some endpoints return a bare array, others wrap it. Find the first array of
        // objects rather than requiring every caller to know which.
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    root = prop.Value;
                    break;
                }
        }

        if (root.ValueKind != JsonValueKind.Array) return [];

        var found = new List<(string, string)>();
        foreach (var el in root.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;

            var id = el.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var name = NameFields
                .Select(f => el.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (!string.IsNullOrWhiteSpace(name)) found.Add((id!, name!));
        }

        return found;
    }

    private static string Join(IEnumerable<string> names)
    {
        var list = names.Take(8).ToList();
        return string.Join(", ", list);
    }
}
