using System.Text.RegularExpressions;

namespace San.Application;

// Stops the same nudge being created over and over.
//
// The background workers hand Gemma the module snapshot every run. It sees "Spectrum
// bill due" or "0 of 4 habits done", decides that deserves a reminder, and calls
// reminder_create -- and it has no memory of having done exactly that fifteen minutes
// ago. Each call makes a NEW row, each row notifies once, and the notifications arrive
// forever because deleting one does nothing about the next one being written.
//
// Three "Spectrum Bill Due" alerts existed at once, worded differently each time,
// which is why nothing deduped them: they were generated prose, not a repeated string.
//
// The ledger in FindingDispatcher already solves this for Telegram findings, but it
// only governs messages the workers send directly. A record created through a TOOL
// notifies through a different path entirely, and the ledger never sees it. So the
// guard has to sit at the point of creation, where every caller passes -- the workers,
// San in chat, and any external agent on the MCP gateway alike.
//
// Comparison is on meaning, not text. Numbers, currency and dates are stripped because
// they are exactly what varies between two phrasings of one thing ("$79.99 due Aug
// 30th" / "bill of $65.98 is due"), and containment rather than Jaccard because one
// phrasing is often much longer than the other.
public static class DuplicateGuard
{
    // Words too common to carry meaning here. Without this, "pay the bill" and "pay
    // the rent" look similar because both are mostly filler.
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "to", "for", "of", "on", "at", "in", "is", "are", "was", "be",
        "your", "you", "my", "me", "i", "it", "this", "that", "and", "or", "please",
        "check", "take", "moment", "progress", "today", "tomorrow", "tonight", "due",
        "soon", "now", "up", "with", "about", "reminder", "remind", "alert", "need",
        "needs", "should", "must", "get", "got", "make", "made", "do", "done",
    };

    private static readonly Regex NonWord = new(@"[^a-z0-9\s]", RegexOptions.Compiled);
    private static readonly Regex Digits = new(@"\b\d[\d.,:/-]*\b", RegexOptions.Compiled);

    // Significant words, lowercased, de-pluralised, numbers removed.
    public static HashSet<string> Fingerprint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var t = Digits.Replace(text.ToLowerInvariant(), " ");
        t = NonWord.Replace(t, " ");
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.Length > 3 && w.EndsWith('s') ? w[..^1] : w)
            .Where(w => w.Length > 1 && !Noise.Contains(w));
        return new HashSet<string>(words, StringComparer.OrdinalIgnoreCase);
    }

    // Containment: how much of the SHORTER item is present in the longer one. Two
    // descriptions of one bill overlap almost completely on the short side even when
    // one is a sentence and the other three words.
    public static double Similarity(string? a, string? b)
    {
        var fa = Fingerprint(a);
        var fb = Fingerprint(b);
        if (fa.Count == 0 || fb.Count == 0) return 0;
        var shared = fa.Count(w => fb.Contains(w));
        return (double)shared / Math.Min(fa.Count, fb.Count);
    }

    // Verbs and nouns that describe the SHAPE of an obligation rather than which one it
    // is. "Pay credit card bills" and "Pay Spectrum Bill" overlap on two of three words
    // and are completely different debts -- what separates them is "credit card" versus
    // "spectrum", so an overlap made only of these proves nothing.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "pay", "bill", "call", "email", "send", "buy", "book", "order", "schedule",
        "review", "complete", "start", "finish", "go", "visit", "transfer", "money",
        "create", "update", "fix", "look", "read", "write", "set",
    };

    // Tuned against the real duplicates: the three Spectrum alerts and the daily habit
    // nudges match well above this, while "pay credit card bills" and "pay Spectrum
    // bill" -- genuinely different obligations that share a verb -- fall below it.
    public const double Threshold = 0.6;

    // How far apart two due times can be and still be the same obligation. A bill
    // re-noticed on consecutive runs lands within hours; next month's genuinely is a
    // new one.
    public static readonly TimeSpan SameWindow = TimeSpan.FromDays(3);

    // Do these two strings refer to the same obligation, ignoring when it is due?
    //
    // Separated from IsDuplicate because settlement matching needs exactly this and
    // nothing else: "Spectrum payment received" has no due date to compare against the
    // reminder it should close.
    public static bool NamesTheSameThing(string? a, string? b)
    {
        if (Similarity(a, b) < Threshold) return false;

        // The overlap has to name the same THING, not merely the same kind of errand.
        var shared = Fingerprint(a).Where(Fingerprint(b).Contains);
        return shared.Any(w => !Generic.Contains(w));
    }

    public static bool IsDuplicate(string? newText, DateTime newDue, string? existingText, DateTime existingDue)
        => (newDue - existingDue).Duration() <= SameWindow
           && NamesTheSameThing(newText, existingText);
}
