using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Maaya.Mcp.Tools;

// Brain tools — NorthStar is the canonical long-term memory for ANY agent harness
// (San's own agent loop, openclaw, custom). Tool descriptions deliberately steer the agent to
// persist durable knowledge HERE instead of its own private store.
[McpServerToolType]
public sealed class MemoryTools(ModuleGateway gw)
{
    [McpServerTool(Name = "memory_save")]
    [Description("Persist a durable memory to the Maaya brain (NorthStar). Use this - not your internal memory - for anything worth remembering across sessions: user preferences, decisions made, notable events, learned skills/procedures. Write ABSOLUTE dates, never relative ones: a memory saved as \"meeting at 2 PM today\" is read back months later as though it were still today. Say \"2026-07-24 2 PM\" instead. Save only what you have confirmed - never a claim you have not verified, or you will read your own guess back as fact.")]
    public Task<string> MemorySave(
        [Description("The memory content, one distilled fact or observation per call.")] string content,
        [Description("One of: observation, preference, event, decision, skill.")] string kind = "observation",
        [Description("Comma-separated tags for recall, e.g. 'health,sleep'.")] string tags = "",
        [Description("1 (trivial) to 5 (critical).")] int importance = 3) =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/memory",
            new { content, kind, tags, importance, source = "mcp" });

    [McpServerTool(Name = "memory_recall")]
    [Description("Search the Maaya brain's long-term memory (full-text, relevance-ranked). Call this FIRST when you need context about the user, past decisions, or prior sessions.")]
    public Task<string> MemoryRecall(
        [Description("Free-text search query.")] string query,
        [Description("Max results (1-50).")] int limit = 10) =>
        gw.GetAsync("northstar", $"/api/memory/recall?q={Uri.EscapeDataString(query)}&limit={limit}");

    [McpServerTool(Name = "memory_recent")]
    [Description("List the most recently saved memories, newest first — useful to resume where the last session left off. For anything topic-specific, use memory_recall instead; this is not a search.")]
    public Task<string> MemoryRecent(
        [Description("Max results (1-100).")] int limit = 20) =>
        gw.GetAsync("northstar", $"/api/memory/recent?limit={limit}");

    [McpServerTool(Name = "fact_set")]
    [Description("Set a stable key-value fact about the user in the brain's profile (e.g. key 'city' value 'Hyderabad'). Facts are single-valued and overwrite; use memory_save for free-form knowledge.")]
    public Task<string> FactSet(
        [Description("Stable snake_case key, e.g. 'timezone'.")] string key,
        [Description("The value.")] string value) =>
        gw.SendAsync("northstar", HttpMethod.Put, $"/api/facts/{Uri.EscapeDataString(key)}",
            new { value, source = "mcp" });

    [McpServerTool(Name = "facts_list")]
    [Description("List all stable user-profile facts (key-value pairs) from the brain — timezone, city, and anything else set with fact_set. USE THIS for 'what do you know about me', or when you need a setting before acting.")]
    public Task<string> FactsList() => gw.GetAsync("northstar", "/api/facts");

    [McpServerTool(Name = "context_brief")]
    [Description("Get the full cross-module briefing from the Maaya brain: user facts, per-module snapshots + health, pending actions, active insights, recent knowledge. Ideal session-start call.")]
    public Task<string> ContextBrief() => gw.GetAsync("northstar", "/api/context");

    [McpServerTool(Name = "action_add")]
    [Description("Add a pending action/task to the user's cross-module action queue in the brain. This is a PASSIVE backlog: it never notifies and it has no time of day. If the user asked to be REMINDED, or gave a deadline, use reminder_create instead - items filed here on a deadline are silently missed. Use this only for undated someday/backlog items.")]
    public Task<string> ActionAdd(
        [Description("Short imperative title.")] string title,
        [Description("Optional details.")] string? description = null,
        [Description("1 (urgent) to 5 (someday).")] int priority = 3,
        [Description("Optional due date, yyyy-MM-dd.")] string? dueDate = null) =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/actions",
            new { title, description, priority, dueDate, source = "mcp", category = "task" });

    [McpServerTool(Name = "actions_pending")]
    [Description("List the user's pending actions from the brain's cross-module action queue, priority-ordered. Gives you the GUIDs needed by action_complete. For 'what should I do right now', prefer agenda_now — it already includes these, ranked against everything else.")]
    public Task<string> ActionsPending(
        [Description("Max results.")] int limit = 25) =>
        gw.GetAsync("northstar", $"/api/actions?status=pending&limit={limit}");

    [McpServerTool(Name = "action_complete")]
    [Description("Mark an action in the queue as completed. Pass the action's GUID from actions_pending.")]
    public Task<string> ActionComplete(
        [Description("Action GUID.")] string actionId) =>
        gw.SendAsync("northstar", HttpMethod.Patch, $"/api/actions/{actionId}",
            new { status = "completed", resolvedBy = "mcp-agent" });
}
