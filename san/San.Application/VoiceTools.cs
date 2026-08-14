using San.Application.Interfaces;

namespace San.Application;

// Which tools San is offered on a SPOKEN turn.
//
// The full catalogue is 44 tools and about 4700 tokens — 58% of an 8200-token voice
// prompt — and on this hardware prefill runs at roughly 210 tok/s, so the catalogue
// alone is most of the wait before San says a word. Measured against the live server,
// same prompt shape, cold:
//
//     44 tools   6779 tokens   24.6s
//      8 tools   2029 tokens    6.3s
//      0 tools    981 tokens    5.5s
//
// Typed chat keeps everything. This is only about what is worth carrying when someone
// is standing there waiting for an answer out loud.
//
// Chosen from what actually gets asked by voice — health, what NorthStar has picked
// up, what's on, and the two actions that are natural to say rather than type. The
// time is deliberately absent: it is already in the context block on every turn, so a
// tool for it would be pure cost.
//
// Nothing is lost permanently. A question that needs something else still works when
// typed, and VOICE_TOOLS overrides this list without a rebuild.
public static class VoiceTools
{
    public static readonly string[] Default =
    [
        "vitara_health",      // sleep, readiness, activity — the main voice question
        "memory_recent",      // what NorthStar has learned lately
        "memory_recall",      // and anything specific it remembers
        "agenda_now",         // "what's on / what am I forgetting", already merged + ranked
        "maaya_search",       // one search across documents, property, transactions, memory
        "reminder_create",    // the most natural thing to say out loud
        "workout_log",        // logging a session by voice beats typing it
        "maaya_status",       // "is everything up?"
    ];

    // VOICE_TOOLS accepts a comma-separated list, "all" to keep the whole catalogue, or
    // "none" to offer no tools at all. Unset uses Default.
    public static List<ToolDefinition> Filter(List<ToolDefinition> all)
    {
        var raw = Environment.GetEnvironmentVariable("VOICE_TOOLS")?.Trim();

        if (string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase)) return all;
        if (string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase)) return [];

        var wanted = string.IsNullOrWhiteSpace(raw)
            ? Default
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var set = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
        var kept = all.Where(t => set.Contains(t.Name)).ToList();

        // A name that matches nothing — a typo in VOICE_TOOLS, or a tool renamed in the
        // gateway — would silently leave San mute-handed on every spoken turn. Falling
        // back to the full catalogue is slow, which is a symptom someone notices and can
        // act on; silently having no tools is one nobody sees.
        return kept.Count > 0 ? kept : all;
    }
}
