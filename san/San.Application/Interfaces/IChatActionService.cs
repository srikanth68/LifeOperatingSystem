namespace San.Application.Interfaces;

// Lets San actually DO things — create reminders, alerts, and calendar events —
// instead of only talking about them. Works with any IChatProvider (no dependency
// on a specific model's native function-calling support): the system prompt
// instructs the model to emit a fenced ```action JSON block when the user wants
// something created; ProcessAsync parses that out of the reply, executes it
// against San's own repository, and strips the block before the reply is shown.
public interface IChatActionService
{
    // Appended to the chat system prompt — describes the available tools and the
    // exact JSON shape the model must use to invoke one.
    string ToolInstructions { get; }

    // A short read-only summary of the user's current reminders/alerts/upcoming
    // events, so San can answer "what are my reminders" accurately, not just create new ones.
    Task<string> BuildOwnContextAsync(CancellationToken ct = default);

    // Parses replyText for a fenced action block, executes it if present and valid,
    // and returns the reply with the raw JSON block stripped (a plain-language
    // confirmation is expected to already be part of the model's reply text).
    // On failure, appends a short caveat rather than silently dropping the action.
    Task<string> ProcessAsync(string replyText, CancellationToken ct = default);
}
