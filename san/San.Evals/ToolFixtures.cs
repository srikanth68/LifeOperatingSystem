using System.Text.Json;
using San.Application.Interfaces;

namespace San.Evals;

// A fixed slice of San's tool catalogue, for the tool-selection cases.
//
// A FIXTURE, deliberately, and this is the one thing to understand about it. The real
// catalogue is served by the MCP gateway at runtime and changes whenever a tool is
// added -- journal_log appeared last week. An eval whose tool list moves underneath it
// cannot compare two runs, so a score would drift for reasons that have nothing to do
// with the model. These names and descriptions mirror the live ones; the point is that
// they are frozen.
//
// Eleven rather than forty-four. Enough that choosing correctly is a real decision --
// several plausible-looking wrong answers sit next to each right one -- without the
// suite spending its whole budget re-reading schemas it is not testing.
public static class ToolFixtures
{
    // Loads a catalogue exported from the live MCP source.
    //
    // The eleven-tool fixture below measures whether the model can choose at all. This
    // loads the real forty-eight, which measures the harder question: whether it can
    // still choose when the catalogue is four times bigger and full of near neighbours.
    // The gateway itself is behind an API key and its list grows whenever a tool is
    // added, so the export is frozen to a file rather than fetched.
    public static List<ToolDefinition> LoadFrom(string path)
    {
        using var stream = File.OpenRead(path);
        var raw = JsonSerializer.Deserialize<List<ExportedTool>>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        return raw.Select(t => new ToolDefinition(
            t.Name,
            t.Description,
            t.Parameters.ToDictionary(
                p => p.Name,
                p => new ToolParameter(p.Type, "", p.Required)))).ToList();
    }

    private sealed record ExportedParam(string Name, string Type, bool Required);
    private sealed record ExportedTool(string Name, string Description, List<ExportedParam> Parameters);

    private static ToolDefinition T(string name, string description, params (string Name, string Type, string Desc, bool Req)[] ps)
        => new(name, description, ps.ToDictionary(p => p.Name, p => new ToolParameter(p.Type, p.Desc, p.Req)));

    public static readonly List<ToolDefinition> Catalogue =
    [
        // The one that should absorb most "what's going on" questions. Its whole reason
        // for existing is to stop San calling four single-module tools one at a time,
        // so a case that fans out instead of calling this is a real regression.
        T("agenda_now",
            "What the user should be doing right now: merges calendar, reminders, alerts, actions, tasks and habits, ranked. Use INSTEAD of calling those one by one."),

        T("reminders_list", "Existing reminders. Read-only."),

        T("reminder_create",
            "Create a reminder. text* · dueOn* ISO date-time",
            ("text", "string", "What to be reminded of.", true),
            ("dueOn", "string", "When, as an ISO date-time.", true)),

        T("reminder_complete",
            "Mark a reminder done. reminderId* from reminders_list.",
            ("reminderId", "string", "Id from reminders_list.", true)),

        // The cross-module search. "Where is / how much did I spend on X" should land
        // here rather than on a single module.
        T("maaya_search",
            "One search across everything: documents, property records, transactions and memory. For \"find\", \"where is\", \"how much did I spend on X\". query*",
            ("query", "string", "What to look for.", true)),

        T("aasthi_properties", "Real-estate portfolio: properties, values, profit."),

        T("property_rent_status",
            "Rent and recurring bills per property this month: expected vs received, and what is late."),

        T("vault_summary", "Balances, net worth, cash and debt totals."),

        T("vitara_health", "Sleep, readiness, activity and heart metrics."),

        T("habit_checkin",
            "Record a habit done today. name*",
            ("name", "string", "The habit.", true)),

        T("journal_log",
            "Append to the user's daily log in their own words. text*",
            ("text", "string", "What to record.", true)),
    ];
}
