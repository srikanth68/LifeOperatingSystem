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

    // A spoken turn gets a much smaller window, and it is the single biggest lever on
    // voice latency. Measured against the live server at San's real prompt size:
    //
    //     history 78 messages (~3100 tok)   17-48s per turn
    //     history 24 messages                5-6s
    //     history 12 messages                3.5-5.5s
    //
    // The reason is prefix caching. The window slides — a new message arrives, the
    // oldest leaves — so the history prefix differs every turn and everything from
    // there on must be re-read. A SMALL sliding window still slides, but there is
    // little behind the divergence to re-read, which is why it stays fast.
    //
    // Speech is also the case that needs history least: spoken exchanges are short and
    // recent, and anything durable was distilled into NorthStar and comes back through
    // recall. Paying 3000 tokens of transcript on every utterance buys very little.
    public static int VoiceTokenBudget =>
        int.TryParse(Environment.GetEnvironmentVariable("CHAT_VOICE_TOKEN_BUDGET"), out var t) && t > 0
            ? t : 800;

    // Kept even if they exceed the budget. One pasted stack trace should not be able
    // to erase the conversation around it and leave San answering with no idea what
    // is being discussed.
    public static int MinMessages =>
        int.TryParse(Environment.GetEnvironmentVariable("CHAT_HISTORY_MIN_MESSAGES"), out var m) && m > 0
            ? m : 6;

    public static int EstimateTokens(string? s) =>
        string.IsNullOrEmpty(s) ? 0 : (s.Length / CharsPerToken) + 1;

    // `history` is oldest-first, as stored. Returns the newest slice that fits.
    // tokenBudget overrides the default — voice turns pass VoiceTokenBudget.
    public static List<T> Select<T>(IReadOnlyList<T> history, Func<T, string> content, Func<T, string> role,
        int? tokenBudget = null)
    {
        if (history.Count == 0) return [];

        var budget = tokenBudget ?? TokenBudget;
        var kept = new List<T>();
        var used = 0;

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var cost = EstimateTokens(content(history[i]));
            if (kept.Count >= MinMessages && used + cost > budget) break;
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
