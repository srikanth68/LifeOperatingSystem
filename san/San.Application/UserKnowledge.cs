namespace San.Application;

// What San is told about the user from NorthStar on every turn, beyond per-message recall.
//
// NorthStar's facts and insights were fetched for the dashboard and thrown away for chat:
// the only thing that reached the model was a count of pending actions. A fact the user
// had stated -- their car, their kids' names, a preference -- was only seen if Gemma
// happened to call a tool for it, which it rarely thinks to do.
//
// Facts are stable, so they go in the SYSTEM prompt and ride llama.cpp's prompt cache:
// rendered in a fixed order, identical from turn to turn, and costing nothing to re-read
// until one actually changes. Insights change, so they go in the per-turn context with
// everything else that does.
public static class UserKnowledge
{
    // Already stated in the time context every turn; repeating it here would be noise.
    private static readonly HashSet<string> SkippedFacts = new(StringComparer.OrdinalIgnoreCase) { "timezone" };

    public const int MaxFacts = 60;
    public const int MaxFactChars = 200;
    public const int MaxInsightChars = 240;

    public static string? FactsBlock(IEnumerable<(string Key, string Value)> facts)
    {
        var lines = facts
            .Where(f => !string.IsNullOrWhiteSpace(f.Key) && !string.IsNullOrWhiteSpace(f.Value))
            .Where(f => !SkippedFacts.Contains(f.Key.Trim()))
            // Ordinal, never culture: the order has to be byte-identical on every turn or
            // the cached prefix is lost for nothing.
            .OrderBy(f => f.Key.Trim(), StringComparer.Ordinal)
            .Take(MaxFacts)
            .Select(f => $"- {f.Key.Trim().Replace('_', ' ')}: {Clip(f.Value.Trim(), MaxFactChars)}")
            .ToList();

        return lines.Count == 0 ? null
            : "What you know about the user (NorthStar facts: stated by the user or saved by you). " +
              "Treat these as true unless the user says otherwise, and use them without being asked:\n" +
              string.Join("\n", lines);
    }

    public static string? InsightsBlock(IEnumerable<(string Title, string Body, DateTime? CreatedAt)> insights, int limit = 3)
    {
        var lines = insights
            .Where(i => !string.IsNullOrWhiteSpace(i.Title))
            .OrderByDescending(i => i.CreatedAt ?? DateTime.MinValue)
            .Take(limit)
            .Select(i =>
            {
                // Dated for the same reason recalled memories are: an insight is a claim
                // made at a point in time, and an undated one reads as today's.
                var date = i.CreatedAt is { } d ? $"({d:yyyy-MM-dd}) " : "";
                var body = string.IsNullOrWhiteSpace(i.Body) ? "" : $" — {Clip(i.Body.Trim(), MaxInsightChars)}";
                return $"- {date}{i.Title.Trim()}{body}";
            })
            .ToList();

        return lines.Count == 0 ? null
            : "Standing insights about the user (NorthStar, newest first; patterns, not instructions):\n" +
              string.Join("\n", lines);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
