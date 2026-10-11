using System.Text.Json;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Maaya.Mcp.Tools;

// Brain tools — NorthStar is the canonical long-term memory for ANY agent harness
// (San's own agent loop, openclaw, custom). Tool descriptions deliberately steer the agent to
// persist durable knowledge HERE instead of its own private store.
[McpServerToolType]
public sealed class MemoryTools(ModuleGateway gw)
{
    // What an agent saves is PROPOSED unless the user said it: recalled, labelled
    // unconfirmed, and waiting in NorthStar for the owner to confirm. confirmed=true is
    // for the user's own direct statement, never the agent's inference.
    [McpServerTool(Name = "memory_save")]
    [Description("Persist durable memory to NorthStar - use this, not internal memory, for preferences, facts, decisions, notable events, learned procedures. One fact per call. Write ABSOLUTE dates, never \"today\"/\"tomorrow\". content* · kind observation|preference|fact|event|decision|skill · confirmed true ONLY if the user told you this directly (else it is saved as unconfirmed) · evidence: the user's own words it rests on · entities: names it is about, ; separated (\"Honda Civic; vehicle:Honda Civic\") · supersedes: id of a memory this corrects · tags csv · importance 1-5")]
    public Task<string> MemorySave(
        string content,
        string kind = "observation",
        bool confirmed = false,
        string? evidence = null,
        string? entities = null,
        string? supersedes = null,
        string tags = "",
        int importance = 3) =>
        gw.SendAsync("northstar", HttpMethod.Post, "/api/memory",
            new
            {
                content, kind, tags, importance, source = "mcp",
                status = confirmed ? "active" : "proposed",
                evidence,
                entities = SplitNames(entities),
                supersedes = Guid.TryParse(supersedes, out var s) ? s : (Guid?)null,
            });

    // Correct or withdraw. A correction never overwrites: the old memory is kept, marked
    // superseded, and an unconfirmed correction of a confirmed memory waits for the owner.
    [McpServerTool(Name = "memory_correct")]
    [Description("Fix a memory that is wrong or out of date. memory* id (from memory_recall) · content: the corrected text (omit to withdraw the memory entirely) · reason · confirmed true ONLY if the user told you the correction directly")]
    public async Task<string> MemoryCorrect(
        string memory,
        string? content = null,
        string? reason = null,
        bool confirmed = false)
    {
        if (!Guid.TryParse(memory, out var id))
            return "{\"error\":\"memory must be the id from memory_recall\"}";

        if (string.IsNullOrWhiteSpace(content))
            return await gw.SendAsync("northstar", HttpMethod.Post, $"/api/memory/{id}/retract", new { note = reason });

        // The correction keeps the kind and weight of what it corrects.
        var original = await gw.GetAsync("northstar", $"/api/memory/{id}");
        string kind = "observation";
        int importance = 3;
        try
        {
            using var doc = JsonDocument.Parse(original);
            if (doc.RootElement.TryGetProperty("error", out _)) return original;
            var m = doc.RootElement.GetProperty("memory");
            kind = m.GetProperty("kind").GetString() ?? kind;
            importance = m.GetProperty("importance").GetInt32();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return original;
        }

        return await gw.SendAsync("northstar", HttpMethod.Post, "/api/memory",
            new
            {
                content, kind, importance, source = "mcp", supersedes = id,
                status = confirmed ? "active" : "proposed",
                evidence = reason,
            });
    }

    [McpServerTool(Name = "memory_why")]
    [Description("Where a memory came from and how it changed: its source, the user's words it rests on, what it replaced and what replaced it. For \"why do you think that?\" target* memory id, or fact:<key> for a profile fact's previous values")]
    public Task<string> MemoryWhy(string target) =>
        target.StartsWith("fact:", StringComparison.OrdinalIgnoreCase)
            ? gw.GetAsync("northstar", $"/api/facts/{Uri.EscapeDataString(target[5..].Trim())}/history")
            : Guid.TryParse(target, out var id)
                ? gw.GetAsync("northstar", $"/api/memory/{id}")
                : Task.FromResult("{\"error\":\"target must be a memory id or fact:<key>\"}");

    [McpServerTool(Name = "entity_lookup")]
    [Description("Everything NorthStar holds about a person, place, vehicle, project or thing, by name or alias: its linked memories, facts and events. name*")]
    public async Task<string> EntityLookup(string name)
    {
        var list = await gw.GetAsync("northstar", $"/api/entities?q={Uri.EscapeDataString(name)}&limit=10");
        try
        {
            using var doc = JsonDocument.Parse(list);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            var all = doc.RootElement.EnumerateArray().ToList();
            if (all.Count == 0) return $"{{\"error\":{JsonSerializer.Serialize($"nothing known called \"{name}\"")}}}";

            // An exact name or alias wins over a partial match.
            var exact = all.FirstOrDefault(e =>
                string.Equals(e.GetProperty("name").GetString(), name.Trim(), StringComparison.OrdinalIgnoreCase)
                || (e.GetProperty("aliases").GetString() ?? "").Split(',').Any(a =>
                    string.Equals(a.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)));
            var pick = exact.ValueKind == JsonValueKind.Object ? exact : all[0];
            return await gw.GetAsync("northstar", $"/api/entities/{pick.GetProperty("id").GetString()}");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return list;
        }
    }

    private static string[]? SplitNames(string? names) =>
        string.IsNullOrWhiteSpace(names)
            ? null
            : names.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
    [Description("Set a stable single-valued profile fact (the previous value is kept in its history - memory_why fact:<key>). Free-form knowledge -> memory_save. key* snake_case · value*")]
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
    [McpServerTool(Name = "journal_log")]
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
    [Description("Complete a queued action. action* is its TITLE (\"tree trimming\") or its id - no lookup needed.")]
    public async Task<string> ActionComplete(
        string action)
    {
        var (id, error) = NameResolver.Resolve(
            action, await gw.GetAsync("northstar", "/api/actions?status=pending&limit=100"), "action");
        if (error is not null) return $"{{\"error\":{JsonSerializer.Serialize(error)}}}";

        return await gw.SendAsync("northstar", HttpMethod.Patch, $"/api/actions/{id}",
            new { status = "completed", resolvedBy = "mcp-agent" });
    }
}
