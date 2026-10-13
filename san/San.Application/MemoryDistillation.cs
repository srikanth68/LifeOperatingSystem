using System.Text.RegularExpressions;

namespace San.Application;

// Where a memory came from and how far it can be trusted, sent with it to NorthStar.
//   Status: "proposed" -- San concluded it; it is recalled, labelled unconfirmed, and waits
//           for the owner to confirm. "active" -- the user said so directly.
//   SourceRef: a pointer back to the origin ("san:chat:<first>..<last>").
//   Evidence: the user's own words it was drawn from, kept with the memory so "why do
//           you think that?" has an answer after the chat is gone.
//   Entities: who or what it is about; NorthStar links the memory to each.
//   Supersedes: the memory this one corrects. Proposed, it waits for the owner to
//           confirm before it replaces anything (NorthStar enforces that).
public record MemoryOrigin(
    string Status,
    string? SourceRef = null,
    string? Evidence = null,
    IReadOnlyList<string>? Entities = null,
    Guid? Supersedes = null)
{
    public const string Proposed = "proposed";
    public const string Active = "active";
}

// The pure parts of turning a stretch of chat into memories: reading the model's reply,
// and recording what each memory was drawn from.
public static partial class MemoryDistillation
{
    public record Line(string Kind, string Text, IReadOnlyList<string> Entities);

    public static readonly string[] Kinds = ["preference", "fact", "decision", "event"];

    // `kind|text`, optionally `kind|text|Name; Name`. Anything else on a line is not a
    // memory. Unknown kinds become observations rather than being dropped.
    public static List<Line> Parse(string? reply)
    {
        var lines = new List<Line>();
        if (string.IsNullOrWhiteSpace(reply)) return lines;

        foreach (var raw in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split('|', 3);
            if (parts.Length < 2) continue;
            var kind = parts[0].Trim().TrimStart('-', '*', ' ').ToLowerInvariant();
            var text = parts[1].Trim();
            if (kind.Length == 0 || text.Length < 3) continue;
            if (!Kinds.Contains(kind)) kind = "observation";
            lines.Add(new Line(kind, text, parts.Length == 3 ? Entities(parts[2]) : []));
        }
        return lines;
    }

    // A small model asked for "who or what it is about" names the user and itself more
    // often than anything else. Those are not entities; they would link every memory to
    // one node and make the link meaningless.
    private static readonly HashSet<string> NotEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "the user", "san", "assistant", "the assistant", "me", "i", "you", "none", "n/a", "na",
    };

    private static IReadOnlyList<string> Entities(string raw) =>
        raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.Trim('"', '\'', '.', ' '))
            .Where(n => n.Length is >= 2 and <= 60 && LetterRegex().IsMatch(n) && !NotEntities.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

    // The user's side of the window: durable facts about the user come from what the user
    // said, and San's own replies would mostly repeat them back. Newest last, trimmed from
    // the front when long, because the end of a window is what the model just read.
    public static string Evidence(IEnumerable<(string Role, string Content)> window, int max = 600)
    {
        var said = string.Join(" / ", window
            .Where(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => WhitespaceRegex().Replace(m.Content.Trim(), " ")));
        return said.Length <= max ? said : "…" + said[^(max - 1)..];
    }

    public static string SourceRef(Guid firstMessage, Guid lastMessage) =>
        firstMessage == lastMessage ? $"san:chat:{firstMessage}" : $"san:chat:{firstMessage}..{lastMessage}";

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
