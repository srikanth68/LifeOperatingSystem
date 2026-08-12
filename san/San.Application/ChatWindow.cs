namespace San.Application;

// Chooses how much conversation to resend to the model each turn.
//
// Every chat turn was resending the last 50 messages regardless of size. Counting
// MESSAGES is the wrong unit: fifty short exchanges and fifty pasted logs differ by
// an order of magnitude in cost, and only one of them is affordable. So the window is
// bounded by an estimated token budget instead, which makes the prompt roughly the
// same size every turn no matter what was said.
//
// This is where the prompt weight actually is. Persona, tool descriptions and module
// context together are around 2K tokens and fixed; history is unbounded and grows
// with use, so it is the only part where trimming changes anything.
//
// What is dropped is not lost. Anything durable has already been distilled into
// NorthStar by the memory worker and comes back through relevance-ranked recall,
// which is a better mechanism than resending raw transcript and hoping the model
// notices the relevant line.
public static class ChatWindow
{
    // Rough but stable: ~4 characters per token is the usual English BPE ratio. An
    // exact count would need Gemma's tokenizer in-process, which is not worth a
    // dependency for a budget whose whole job is to be approximately right.
    private const int CharsPerToken = 4;

    public static int TokenBudget =>
        int.TryParse(Environment.GetEnvironmentVariable("CHAT_HISTORY_TOKEN_BUDGET"), out var t) && t > 0
            ? t : 3000;

    // Kept even if they exceed the budget. One pasted stack trace should not be able
    // to erase the conversation around it and leave San answering with no idea what
    // is being discussed.
    public static int MinMessages =>
        int.TryParse(Environment.GetEnvironmentVariable("CHAT_HISTORY_MIN_MESSAGES"), out var m) && m > 0
            ? m : 6;

    public static int EstimateTokens(string? s) =>
        string.IsNullOrEmpty(s) ? 0 : (s.Length / CharsPerToken) + 1;

    // `history` is oldest-first, as stored. Returns the newest slice that fits.
    public static List<T> Select<T>(IReadOnlyList<T> history, Func<T, string> content, Func<T, string> role)
    {
        if (history.Count == 0) return [];

        var kept = new List<T>();
        var used = 0;

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var cost = EstimateTokens(content(history[i]));
            if (kept.Count >= MinMessages && used + cost > TokenBudget) break;
            kept.Add(history[i]);
            used += cost;
        }

        kept.Reverse();

        // Never open the window on an assistant reply: a reply with no visible question
        // reads as though San said it unprompted, and the model will try to make sense
        // of it as context rather than as an answer.
        //
        // This can take the window one below MinMessages, which is deliberate — the
        // floor exists to stop the conversation being erased, not to pad it, and five
        // coherent messages are worth more than six that begin mid-exchange.
        while (kept.Count > 1 && !string.Equals(role(kept[0]), "user", StringComparison.OrdinalIgnoreCase))
            kept.RemoveAt(0);

        return kept;
    }
}
