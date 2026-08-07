using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace San.Application;

public record AgentFinding(string Key, string Severity, string Message, DateTime? DueOn);

// Turns a worker's model reply into discrete, keyed findings. The workers used to
// take the whole reply as one blob of text, which left nothing to dedupe on — a
// reworded repeat looked like a brand-new finding.
public static class FindingParser
{
    public const string NothingImportant = "NOTHING_IMPORTANT";

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<AgentFinding> Parse(string reply)
    {
        var trimmed = (reply ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Equals(NothingImportant, StringComparison.OrdinalIgnoreCase))
            return [];

        var json = ExtractJson(trimmed);
        if (json is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("findings", out var arr))
                    root = arr;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    // Authoritative even when it comes out empty: the model gave us the
                    // shape we asked for and it contained nothing usable. Falling through
                    // here would hand the raw JSON to the prose fallback and send that
                    // verbatim to Telegram.
                    var list = new List<AgentFinding>();
                    foreach (var el in root.EnumerateArray())
                        if (ToFinding(el) is { } f) list.Add(f);
                    return list;
                }
                if (root.ValueKind == JsonValueKind.Object && ToFinding(root) is { } single)
                    return [single];
                // A lone object we can't read as a finding falls through to the prose
                // fallback rather than being dropped — missing a real warning is worse
                // than an ugly one.
            }
            catch (JsonException) { /* fall through to the text fallback */ }
        }

        // Model ignored the format. Still deliver it rather than dropping a possibly
        // real finding — keyed by a hash of the text, which dedupes verbatim repeats
        // but NOT reworded ones. That degradation is the reason the prompt insists
        // on the JSON shape.
        return [new AgentFinding($"text:{ShortHash(trimmed)}", "medium", trimmed, null)];
    }

    private static AgentFinding? ToFinding(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        var message = Str(el, "message") ?? Str(el, "text") ?? "";
        if (string.IsNullOrWhiteSpace(message)) return null;

        var key = Str(el, "key");
        if (string.IsNullOrWhiteSpace(key)) key = $"text:{ShortHash(message)}";

        var severity = (Str(el, "severity") ?? "medium").Trim().ToLowerInvariant();
        if (severity is not ("critical" or "high" or "medium" or "low")) severity = "medium";

        DateTime? due = null;
        if (Str(el, "dueOn") is { Length: > 0 } d &&
            DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            due = parsed;

        return new AgentFinding(key.Trim(), severity, message.Trim(), due);
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Models like to wrap JSON in prose or a ``` fence — take the outermost bracketed span.
    private static string? ExtractJson(string s)
    {
        var starts = new[] { s.IndexOf('{'), s.IndexOf('[') }.Where(i => i >= 0).ToArray();
        if (starts.Length == 0) return null;
        var start = starts.Min();
        var open = s[start];
        var close = open == '{' ? '}' : ']';
        var end = s.LastIndexOf(close);
        return end > start ? s[start..(end + 1)] : null;
    }

    private static string ShortHash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..12].ToLowerInvariant();
}

// Decides whether two findings are about the SAME thing, from their text alone.
//
// The ledger originally keyed off a "stable key" the model was asked to supply. It
// doesn't supply one — the same course deadline came back as "starts tomorrow",
// "cohort starts tomorrow", "begins tomorrow", "is rapidly approaching" and "is
// tomorrow morning" within hours, each with its own key, so nothing ever matched.
// Asking the model for stability is the same mistake as asking it not to repeat
// itself; identity has to be computed here.
public static class TopicSignature
{
    // Ordinary English filler, plus the words these findings ALWAYS contain
    // regardless of subject: urgency, deadlines, and generic money/action nouns.
    // Stripping them is what stops "AMC bill due Aug 9" and "electric bill due
    // Aug 12" collapsing into each other on {bill, due, aug} — what's left is the
    // distinctive part ({amc, theatres} vs {electric}).
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","is","are","was","were","be","been","being","to","for","of","in","on","at",
        "and","or","but","if","then","so","as","by","from","up","about","into","than","that","this",
        "these","those","it","its","your","you","yours","my","me","i","we","our","they","them",
        "will","would","should","shall","can","could","may","might","must","do","does","did","have",
        "has","had","not","no","yes","with","without","there","here","now","new",
        // temporal
        "today","tomorrow","tonight","yesterday","morning","afternoon","evening","day","days","week",
        "weeks","month","months","soon","upcoming","approaching","rapidly","imminent","begins","begin",
        "starts","start","starting","started","ends","end","ending","before","after","during","until",
        "date","dates","deadline","time","times",
        // urgency / action
        "urgent","urgently","critical","important","immediate","immediately","action","required",
        "require","requires","needed","needs","need","please","review","check","ensure","make","sure",
        "attention","alert","reminder","remind","note","noted","warning","warn","asap","must",
        // generic finance / status
        "bill","bills","payment","payments","pay","paid","due","amount","balance","cash","cost",
        "fee","fees","charge","charges","total","status","update","updates",
    };

    public static IReadOnlySet<string> Tokens(string message)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var ch in message ?? "")
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else { Add(set, sb); sb.Clear(); }
        }
        Add(set, sb);
        return set;

        static void Add(HashSet<string> set, StringBuilder sb)
        {
            if (sb.Length < 2) return;
            var w = sb.ToString();
            if (!Stop.Contains(w)) set.Add(w);
        }
    }

    // Overlap coefficient, not Jaccard: the same issue gets described at very
    // different lengths run to run, and Jaccard punishes that by inflating the
    // union. Overlap asks "is the shorter description essentially contained in the
    // longer one", which is the actual question.
    public static double Similarity(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var shared = a.Count <= b.Count ? a.Count(b.Contains) : b.Count(a.Contains);
        return (double)shared / Math.Min(a.Count, b.Count);
    }

    public const double SameTopicThreshold = 0.6;

    // Below this there isn't enough left after stripping to judge similarity on, so
    // we don't guess — the finding keeps its own key.
    public const int MinTokens = 2;
}

// How often a still-true finding may be repeated. Not "once ever" — a bill that is
// still unpaid should resurface, just on a sane cadence rather than every 15 minutes.
public static class NotifyPolicy
{
    private static double Hours(string env, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(env), out var h) && h > 0 ? h : fallback;

    public static TimeSpan BaseCooldown(string severity) => TimeSpan.FromHours(severity switch
    {
        "critical" => Hours("NOTIFY_COOLDOWN_CRITICAL_HOURS", 6),
        "high"     => Hours("NOTIFY_COOLDOWN_HIGH_HOURS", 12),
        "low"      => Hours("NOTIFY_COOLDOWN_LOW_HOURS", 72),
        _          => Hours("NOTIFY_COOLDOWN_MEDIUM_HOURS", 24),
    });

    // Something the user keeps not acting on gets progressively quieter — up to 3x
    // the base gap — so a long-standing condition doesn't nag at full volume forever.
    // Suspended within 48h of a real deadline, where the steady cadence is the point.
    public static TimeSpan Cooldown(string severity, int notifyCount, DateTime? dueOn, DateTime nowUtc)
    {
        var b = BaseCooldown(severity);
        var deadlineNear = dueOn is { } d && d - nowUtc <= TimeSpan.FromHours(48) && d >= nowUtc.AddHours(-24);
        if (deadlineNear) return b;
        var factor = Math.Min(Math.Max(notifyCount, 1), 3);
        return b * factor;
    }

    public static bool ShouldNotify(string severity, int notifyCount, DateTime lastNotifiedAt, DateTime? dueOn, DateTime nowUtc) =>
        nowUtc - lastNotifiedAt >= Cooldown(severity, notifyCount, dueOn, nowUtc);
}
