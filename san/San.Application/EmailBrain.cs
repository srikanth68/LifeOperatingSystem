using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace San.Application;

// The pure parts of reading one email properly: turning its body into text worth a
// model's attention, laying out what NorthStar already knows beside it, and reading
// back what the model concluded.
//
// Triage used to see a ~200-character snippet of each email in one batch, and nothing
// of what San already knew. A bill whose amount sat in the second paragraph, or a
// reply that only made sense next to the message it answered, was judged from its
// first line. Now each email that survives the bulk filter is read in full, one at a
// time, beside the entities, memories and earlier messages it relates to -- and what
// it teaches is written back to NorthStar as a dated event, proposed memories with
// the email as their source, and proposed corrections to memories it contradicts.
// The email itself is never stored.
public static partial class EmailText
{
    // Enough for any real bill, statement or letter; long marketing tails and legal
    // footers past it are not where the facts are.
    public const int MaxChars = 6000;

    public static string ToPlainText(string? body, bool isHtml)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        if (!isHtml) return body;

        var s = ScriptOrStyle().Replace(body, " ");
        s = BlockEnd().Replace(s, "\n");
        s = Tag().Replace(s, " ");
        return WebUtility.HtmlDecode(s);
    }

    // Cuts the quoted conversation below a reply and the signature, and squeezes the
    // whitespace HTML leaves behind. The earlier messages in a thread are not lost:
    // NorthStar holds them as events and they are shown beside this one.
    public static string Clean(string? text, int max = MaxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var kept = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (QuoteStart().IsMatch(line)) break;     // "On Tue, ... wrote:" / Outlook's header block
            if (line == "-- " || line == "--") break;   // RFC 3676 signature separator
            if (line.TrimStart().StartsWith('>')) continue;
            kept.Add(Spaces().Replace(line, " ").Trim());
        }

        var joined = BlankRuns().Replace(string.Join("\n", kept), "\n\n").Trim();
        return joined.Length <= max ? joined : joined[..max].TrimEnd() + "\n[…cut: the rest of this email is longer than San reads]";
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<\s*(br|/p|/div|/tr|/li|/h[1-6]|/table)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^\s*(On\s.{4,200}\swrote:\s*$|-{2,}\s*Original Message\s*-{2,}|From:\s.+\s(Sent|Date):\s)", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteStart();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();
}

public static partial class EmailSender
{
    // "Acme Billing <billing@acme.com>" → ("Acme Billing", "billing@acme.com").
    // A bare address has no display name.
    public static (string? Name, string? Address) Parse(string? from)
    {
        if (string.IsNullOrWhiteSpace(from)) return (null, null);
        var m = Angle().Match(from);
        if (m.Success)
        {
            var name = m.Groups[1].Value.Trim().Trim('"', '\'').Trim();
            return (name.Length > 0 ? name : null, m.Groups[2].Value.Trim().ToLowerInvariant());
        }
        var bare = from.Trim();
        return bare.Contains('@') ? (null, bare.ToLowerInvariant()) : (bare, null);
    }

    [GeneratedRegex(@"^(.*?)<\s*([^<>\s]+@[^<>\s]+)\s*>")]
    private static partial Regex Angle();
}

// What the model concluded about one email, beyond the reminders and events it made
// with its tools.
public record EmailNotes(
    string Importance,
    string? Summary,
    IReadOnlyList<EmailNotes.Lasting> Remember,
    IReadOnlyList<EmailNotes.Correction> Corrects)
{
    public record Lasting(string Kind, string Text, IReadOnlyList<string> About);
    public record Correction(Guid Memory, string Text);

    public const string Ignore = "ignore";
    public const string Note = "note";
    public const string Act = "act";

    public bool Keep => Importance != Ignore;

    private static readonly string[] Kinds = ["fact", "preference", "decision", "event"];

    // Tolerant on purpose: a small local model returns most of the shape most of the
    // time. Anything unreadable degrades to "note" with no summary, so the email is
    // still recorded as having arrived rather than vanishing.
    public static EmailNotes Parse(string? reply)
    {
        var trimmed = (reply ?? "").Trim();
        if (trimmed.Equals(FindingParser.NothingImportant, StringComparison.OrdinalIgnoreCase))
            return new(Ignore, null, [], []);

        var json = FindingParser.ExtractJson(trimmed);
        if (json is null) return new(Note, null, [], []);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(Note, null, [], []);

            var importance = Str(root, "importance")?.ToLowerInvariant() switch
            {
                Ignore => Ignore,
                Act => Act,
                _ => Note,
            };

            var remember = new List<Lasting>();
            if (root.TryGetProperty("remember", out var rem) && rem.ValueKind == JsonValueKind.Array)
                foreach (var r in rem.EnumerateArray())
                {
                    var text = Str(r, "text");
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 4) continue;
                    var kind = Str(r, "kind")?.ToLowerInvariant();
                    remember.Add(new Lasting(
                        Kinds.Contains(kind) ? kind! : "fact",
                        text.Trim(),
                        Names(r, "about")));
                }

            var corrects = new List<Correction>();
            if (root.TryGetProperty("corrects", out var cor) && cor.ValueKind == JsonValueKind.Array)
                foreach (var c in cor.EnumerateArray())
                    if (Guid.TryParse(Str(c, "memory"), out var id) && Str(c, "text") is { Length: >= 4 } t)
                        corrects.Add(new Correction(id, t.Trim()));

            var summary = Str(root, "summary")?.Trim();
            return new(importance, string.IsNullOrWhiteSpace(summary) ? null : summary, remember, corrects);
        }
        catch (JsonException)
        {
            return new(Note, null, [], []);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static IReadOnlyList<string> Names(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return [];
        var raw = v.ValueKind switch
        {
            JsonValueKind.Array => v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? ""),
            JsonValueKind.String => (v.GetString() ?? "").Split([';', ',']),
            _ => [],
        };
        return raw.Select(n => n.Trim()).Where(n => n.Length >= 2 && n.Length <= 60)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }
}

public static class EmailBrainPrompt
{
    // The stable key and the line NorthStar keeps for each email worth keeping. The
    // Detail carries the sender and thread so the next email from either finds it.
    public static string EventKey(string provider, string messageId) => $"email:{provider}:{messageId}";

    public static string SourceRef(string provider, string messageId) => $"email:{provider}:{messageId}";

    public static string Detail(string? senderAddress, string? threadId, string subject) =>
        string.Join(" · ", new[]
        {
            senderAddress is null ? null : $"from {senderAddress}",
            string.IsNullOrWhiteSpace(threadId) ? null : $"thread {threadId}",
            subject,
        }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public static string UserTurn(string account, string from, string subject, DateTime receivedUtc, string body, string? related)
    {
        var sb = new StringBuilder();
        sb.Append("A new email arrived in ").Append(account).Append(".\n\n");
        sb.Append("From: ").Append(from).Append('\n');
        sb.Append("Subject: ").Append(subject).Append('\n');
        sb.Append("Received: ").Append(receivedUtc.ToString("u")).Append("\n\n");
        sb.Append(string.IsNullOrWhiteSpace(body) ? "(no readable body)" : body).Append("\n\n");
        sb.Append(related ?? "WHAT NORTHSTAR ALREADY KNOWS: nothing related.");
        return sb.ToString();
    }

    // The block of related knowledge, from NorthStar's /api/context/related. Memory
    // ids are shown because a correction has to name the memory it replaces.
    public static string? Related(JsonElement? related)
    {
        if (related is not { ValueKind: JsonValueKind.Object } r) return null;
        var lines = new List<string>();

        if (r.TryGetProperty("earlier", out var earlier) && earlier.ValueKind == JsonValueKind.Array)
            foreach (var e in earlier.EnumerateArray())
                lines.Add($"- earlier email ({Date(e, "occurredAt")}): {Text(e, "title")}");

        if (r.TryGetProperty("memories", out var mems) && mems.ValueKind == JsonValueKind.Array)
            foreach (var m in mems.EnumerateArray())
            {
                var unconfirmed = Text(m, "status") == "proposed" ? ", unconfirmed" : "";
                lines.Add($"- memory {Text(m, "id")} ({Text(m, "kind")}, saved {Date(m, "createdAt")}{unconfirmed}): {Text(m, "content")}");
            }

        if (r.TryGetProperty("entities", out var ents) && ents.ValueKind == JsonValueKind.Array)
            foreach (var e in ents.EnumerateArray())
            {
                var linked = e.TryGetProperty("linked", out var l) && l.ValueKind == JsonValueKind.Array
                    ? string.Join("; ", l.EnumerateArray().Select(x => Text(x, "summary")).Where(x => x.Length > 0).Take(4))
                    : "";
                lines.Add($"- {Text(e, "type")} \"{Text(e, "name")}\"" + (linked.Length > 0 ? $": {linked}" : ""));
            }

        return lines.Count == 0
            ? null
            : "WHAT NORTHSTAR ALREADY KNOWS (related to this email):\n" + string.Join("\n", lines);
    }

    // Entities NorthStar recognised in the email, by name: the email's event is linked
    // to them, so "everything about the flat" includes the emails about it.
    public static IReadOnlyList<string> RelatedEntityNames(JsonElement? related) =>
        related is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("entities", out var ents)
            && ents.ValueKind == JsonValueKind.Array
            ? ents.EnumerateArray().Select(e => Text(e, "name")).Where(n => n.Length > 0).ToList()
            : [];

    // The memories that were shown, by id, with their kind. A correction may only name
    // one of these: a model that invents an id, or reaches for a memory it was never
    // shown, does not get to replace it.
    public static IReadOnlyDictionary<Guid, string> RelatedMemoryKinds(JsonElement? related)
    {
        var map = new Dictionary<Guid, string>();
        if (related is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("memories", out var mems)
            && mems.ValueKind == JsonValueKind.Array)
            foreach (var m in mems.EnumerateArray())
                if (Guid.TryParse(Text(m, "id"), out var id))
                    map[id] = Text(m, "kind") is { Length: > 0 } k ? k : "fact";
        return map;
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Date(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var d)
            ? d.ToString("yyyy-MM-dd") : "?";
}
