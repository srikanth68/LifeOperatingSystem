using San.Application.Interfaces;

namespace San.Application;

// Which tools San is offered on a TYPED turn.
//
// The mirror image of VoiceTools: that one names the few worth carrying when someone
// is waiting to hear an answer, this one names the few not worth carrying at all.
// Typed chat can afford the catalogue, but "can afford" is not "free" -- the tool
// block is the largest single component of the prompt, and roughly half of it is JSON
// scaffolding that no amount of rewording touches. Dropping a tool is the only thing
// that removes scaffolding.
//
// Nothing is deleted. The gateway still publishes all 44 to any other agent that
// connects to it, and CHAT_TOOLS_EXCLUDE overrides this list without a rebuild -- set
// it empty to get everything back.
public static class ChatTools
{
    public static readonly string[] ExcludedByDefault =
    [
        // Contact management, dropped at the user's request. San can still be told
        // about people through memory; it just no longer reads or edits the address
        // book, which means the ~490 imported contacts are invisible to chat until
        // san_people comes back. Reversible with CHAT_TOOLS_EXCLUDE.
        "person_create",
        "person_update",
        "person_delete",
        "san_people",
    ];

    // CHAT_TOOLS_EXCLUDE accepts a comma-separated list of tool names to drop, or
    // "none" to keep the whole catalogue. Unset uses ExcludedByDefault.
    public static List<ToolDefinition> Filter(List<ToolDefinition> all)
    {
        var raw = Environment.GetEnvironmentVariable("CHAT_TOOLS_EXCLUDE")?.Trim();
        if (string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)) return all;

        var excluded = string.IsNullOrWhiteSpace(raw)
            ? ExcludedByDefault
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var set = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        var kept = all.Where(t => !set.Contains(t.Name)).ToList();

        // Excluding everything is always a mistake rather than an intention -- a bad
        // env value, or a catalogue that shrank. Unlike the voice path, where falling
        // back to the full set is merely slow, here it is also the correct answer.
        return kept.Count > 0 ? kept : all;
    }
}
