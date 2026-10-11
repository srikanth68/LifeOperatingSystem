namespace San.Application;

// How recalled memories are shown to the model on a chat turn.
//
// Every line carries the date it was SAVED, and this is not optional. A memory saved on
// 24 July read "Meeting with ECSE team scheduled for 2 PM today." It was true that day.
// Recalled undated 39 times since, San reported a 2 PM meeting on each of them --
// correctly, given what it was shown, because nothing in the line said the sentence was
// three weeks old. The date cannot repair a memory that says "today", but it makes the
// staleness visible: San's time context already states the current date.
//
// And every line San inferred rather than was told says so. NorthStar recalls proposed
// memories -- San is better off knowing them -- but a guess presented in the same voice
// as a fact the user stated is how a guess gets repeated back to the user as one.
public static class RecalledMemories
{
    public record Item(string Content, string? Kind, DateTime? SavedUtc, string? Status);

    public static string? Format(IEnumerable<Item> items)
    {
        var lines = new List<string>();
        var anyUnconfirmed = false;
        foreach (var m in items)
        {
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            var unconfirmed = string.Equals(m.Status, "proposed", StringComparison.OrdinalIgnoreCase);
            anyUnconfirmed |= unconfirmed;

            var tag = string.Join(", ", new[]
            {
                m.Kind,
                m.SavedUtc?.ToString("yyyy-MM-dd"),
                unconfirmed ? "unconfirmed" : null,
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            lines.Add(tag.Length > 0 ? $"- ({tag}) {m.Content}" : $"- {m.Content}");
        }

        if (lines.Count == 0) return null;

        // Said once for the whole block rather than per line, and said at all because the
        // model otherwise reads a recalled sentence as present tense.
        var header = "(each line is dated with when it was SAVED - a memory saying \"today\" means "
                   + "the day it was saved, not now"
                   + (anyUnconfirmed
                       ? "; \"unconfirmed\" means you inferred it and the user has not confirmed it - "
                         + "do not state it to them as fact"
                       : "")
                   + ")";
        return header + "\n" + string.Join("\n", lines);
    }
}
