using San.Application;
using San.Application.Interfaces;

namespace San.Tests;

// The whole point is offering fewer tools on a spoken turn, so the risk is offering
// none — which would leave San unable to answer anything by voice, silently.
public class VoiceToolsTests
{
    private static List<ToolDefinition> Catalogue(params string[] names) =>
        names.Select(n => new ToolDefinition(n, $"does {n}", new Dictionary<string, ToolParameter>())).ToList();

    private static readonly List<ToolDefinition> Full =
        Catalogue("vitara_read", "memory_recent", "memory_recall", "agenda_now", "maaya_search",
                  "reminder_create", "workout_log", "maaya_status", "journal_log",
                  "vault_read", "property_task_create", "person_create", "food_log", "nexus_read");

    [Fact]
    public void KeepsOnlyTheVoiceSet()
    {
        var kept = VoiceTools.Filter(Full).Select(t => t.Name).ToList();
        Assert.Contains("vitara_read", kept);
        Assert.Contains("memory_recent", kept);
        Assert.DoesNotContain("vault_read", kept);
        Assert.DoesNotContain("nexus_read", kept);
    }

    [Fact]
    public void CutsTheCatalogueSubstantially()
    {
        var kept = VoiceTools.Filter(Full);
        Assert.True(kept.Count < Full.Count);
        Assert.Equal(VoiceTools.Default.Length, kept.Count);
    }

    // The failure that would be invisible: a typo in VOICE_TOOLS, or a tool renamed in
    // the gateway, leaving San with nothing on every spoken turn. Falling back to the
    // full catalogue is slow — a symptom someone notices — rather than mute.
    [Fact]
    public void FallsBackToEverythingWhenNothingMatches()
    {
        var unrelated = Catalogue("something_else", "another_thing");
        var kept = VoiceTools.Filter(unrelated);
        Assert.Equal(unrelated.Count, kept.Count);
    }

    [Fact]
    public void EmptyCatalogueStaysEmpty()
        => Assert.Empty(VoiceTools.Filter([]));

    // Every default name should be a real tool, not a typo that silently does nothing.
    // These are the names the MCP gateway actually serves.
    [Fact]
    public void DefaultsAreAllRealToolNames()
    {
        string[] gateway =
        [
            "aasthi_read", "action_add", "action_complete", "actions_pending", "agenda_now", "alert_create",
            "alert_delete", "alerts_list", "calendar_event_create", "calendar_events_list", "context_brief",
            "entity_lookup", "fact_set", "facts_list", "food_log", "goal_create", "goal_progress_set",
            "habit_checkin", "habit_create", "health_baselines", "health_findings", "health_forecast",
            "health_signature", "health_visit_brief", "journal_log", "journal_read", "karma_read",
            "maaya_search", "maaya_status", "memory_correct", "memory_recall", "memory_recent", "memory_save",
            "memory_why", "nexus_read", "northstar_sync", "person_create", "person_delete", "person_update",
            "property_financial_add", "property_task_create", "reminder_complete", "reminder_create",
            "reminder_delete", "reminder_update", "reminders_list", "san_people", "sutra_read", "vault_read",
            "vitara_read", "weight_log", "workout_log",
        ];
        foreach (var name in VoiceTools.Default)
            Assert.Contains(name, gateway);
    }
}
