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
    [Description("Persist durable memory to NorthStar - use this, not internal memory, for preferences, decisions, notable events, learned procedures. One fact per call. Write ABSOLUTE dates, never \"today\"/\"tomorrow\". Save only what you have confirmed. content* · kind observation|preference|event|decision|skill · tags csv · importance 1-5")]
    public Task<string> MemorySave(
        string content,
        string kind = "observation",
        string tags = "",
        int importance = 3) =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/memory",
            new { content, kind, tags, importance, source = "mcp" });

    [McpServerTool(Name = "memory_recall")]
    [Description("Full-text relevance-ranked search of long-term memory. Call FIRST when you need context on the user, past decisions, or prior sessions. query* · limit 1-50")]
    public Task<string> MemoryRecall(
        string query,
        int limit = 10) =>
        gw.GetAsync("northstar", $"/api/memory/recall?q={Uri.EscapeDataString(query)}&limit={limit}");

    [McpServerTool(Name = "memory_recent")]
    [Description("Most recent memories, newest first - resume where the last session ended. Not a search; topic-specific -> memory_recall. limit 1-100")]
    public Task<string> MemoryRecent(
        int limit = 20) =>
        gw.GetAsync("northstar", $"/api/memory/recent?limit={limit}");

    [McpServerTool(Name = "fact_set")]
    [Description("Set a stable single-valued profile fact (overwrites). Free-form knowledge -> memory_save. key* snake_case · value*")]
    public Task<string> FactSet(
        string key,
        string value) =>
        gw.SendAsync("northstar", HttpMethod.Put, $"/api/facts/{Uri.EscapeDataString(key)}",
            new { value, source = "mcp" });

    [McpServerTool(Name = "facts_list")]
    [Description("All profile key-value facts. For \"what do you know about me\", or to read a setting before acting.")]
    public Task<string> FactsList() => gw.GetAsync("northstar", "/api/facts");

    // The user's own daily log. Kept out of memory_save on purpose: memories are
    // relevance-ranked into San's context on every turn, and a diary retrieved by
    // accident during a reminder request is how the model learns to answer with prose
    // instead of a tool call.
    [McpServerTool(Name = "journal_add")]
    [Description("Append to the user's daily journal. USE THIS when they are recounting their day, thinking out loud, or say to log/journal something - not memory_save, which is for durable facts and is read back on every turn. Entries append, so several a day is normal. text* · day yyyy-MM-dd (defaults to today)")]
    public Task<string> JournalAdd(
        string text,
        string? day = null) =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/journal",
            new { text, day, source = "san" });

    [McpServerTool(Name = "journal_read")]
    [Description("Read back the daily journal, newest first. USE THIS for 'what did I do last week', 'what was I thinking about on Tuesday', or before any review or reflection. days (14) · limit (50)")]
    public Task<string> JournalRead(
        int days = 14,
        int limit = 50) =>
        gw.GetAsync("northstar", $"/api/journal?days={days}&limit={limit}");

    [McpServerTool(Name = "context_brief")]
    [Description("Full cross-module briefing: user facts, module snapshots + health, pending actions, active insights, recent knowledge. Ideal session-start call.")]
    public Task<string> ContextBrief() => gw.GetAsync("northstar", "/api/context");

    [McpServerTool(Name = "action_add")]
    [Description("Queue a passive backlog task (never notifies - for \"remind me\" use reminder_create). title* · description · priority 1 urgent-5 someday · dueDate")]
    public Task<string> ActionAdd(
        string title,
        string? description = null,
        int priority = 3,
        string? dueDate = null) =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/actions",
            new { title, description, priority, dueDate, source = "mcp", category = "task" });

    [McpServerTool(Name = "actions_pending")]
    [Description("Pending actions, priority-ordered, with GUIDs. For \"what should I do now\" prefer agenda_now. limit")]
    public Task<string> ActionsPending(
        int limit = 25) =>
        gw.GetAsync("northstar", $"/api/actions?status=pending&limit={limit}");

    [McpServerTool(Name = "action_complete")]
    [Description("Complete a queued action. actionId* from actions_pending.")]
    public Task<string> ActionComplete(
        string actionId) =>
        gw.SendAsync("northstar", HttpMethod.Patch, $"/api/actions/{actionId}",
            new { status = "completed", resolvedBy = "mcp-agent" });
}
