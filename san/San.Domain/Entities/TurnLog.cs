namespace San.Domain.Entities;

// One assistant turn, kept for training.
//
// San has always thrown this away. ChatMessage stores role, content and a timestamp,
// so the one thing a fine-tune actually needs -- this prompt produced this tool call
// with these arguments -- was computed on every single turn and discarded when the
// request ended. Months of real usage, gone, and unrecoverable afterwards: you cannot
// go back and ask what the model did in March.
//
// That is why this exists before any training work. Data has to start accruing before
// it can be used, and every day it is off is a day that cannot be got back.
//
// Kept deliberately separate from ChatMessage. Chat history is a conversation the user
// reads and clears; this is a record of what the model DID, and clearing the chat must
// not wipe the dataset.
public class TurnLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // "chat" or "voice". They carry different prompts and different tool sets, so a
    // dataset that mixes them without saying which is which cannot be split later.
    public string Source { get; set; } = "chat";

    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";

    public string UserMessage { get; set; } = "";
    public string AssistantReply { get; set; } = "";

    // The tool calls the turn actually made, in order: name, arguments, and a clipped
    // result. This is the training signal -- everything else here is context for it.
    public string ToolCallsJson { get; set; } = "[]";
    public int ToolCallCount { get; set; }

    // What the model was OFFERED, by name. The full schemas are several thousand tokens
    // and identical between turns, so storing them per row would be mostly duplication;
    // the names are enough to know whether the right tool was even available, which is
    // the question worth asking of a turn that failed to call one.
    public string OfferedTools { get; set; } = "";

    // WriteClaimCheck's verdict on this turn, recomputed over what really ran.
    //
    // The most valuable column here. Every true is a labelled failure -- the model said
    // it did something and the tool record says otherwise -- with the correct behaviour
    // already known. That is a training example nobody had to hand-annotate, which is
    // the whole argument for keeping the guardrails while working towards a model that
    // does not need them.
    public bool ClaimedUnverifiedWrite { get; set; }

    public long LlmMs { get; set; }
    public int PromptChars { get; set; }
}
