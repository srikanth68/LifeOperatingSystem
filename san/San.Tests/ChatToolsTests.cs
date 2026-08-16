using San.Application;
using San.Application.Interfaces;

namespace San.Tests;

// The mirror of VoiceToolsTests. The risk here is the opposite one: voice fails by
// offering too few tools, chat fails by quietly dropping one that was still wanted.
public class ChatToolsTests
{
    private static List<ToolDefinition> Catalogue(params string[] names) =>
        names.Select(n => new ToolDefinition(n, $"does {n}", new Dictionary<string, ToolParameter>())).ToList();

    private static readonly string[] All =
        ["reminder_create", "agenda_now", "vitara_health", "maaya_status", "maaya_search",
         "person_create", "person_update", "person_delete", "san_people"];

    [Fact]
    public void DropsTheWholePeopleSection()
    {
        var kept = ChatTools.Filter(Catalogue(All)).Select(t => t.Name).ToList();
        Assert.DoesNotContain("person_create", kept);
        Assert.DoesNotContain("person_update", kept);
        Assert.DoesNotContain("person_delete", kept);
        Assert.DoesNotContain("san_people", kept);
    }

    [Fact]
    public void KeepsEverythingElse()
    {
        var kept = ChatTools.Filter(Catalogue(All)).Select(t => t.Name).ToList();
        Assert.Equal(["reminder_create", "agenda_now", "vitara_health", "maaya_status", "maaya_search"], kept);
    }

    [Fact]
    public void KeepsMaayaStatusWhichTheUserAskedToKeep()
        => Assert.Contains("maaya_status", ChatTools.Filter(Catalogue(All)).Select(t => t.Name));

    [Fact]
    public void ExcludingEverythingFallsBackToTheFullCatalogue()
    {
        var only = Catalogue("person_create", "san_people");
        Assert.Equal(2, ChatTools.Filter(only).Count);
    }

    [Fact]
    public void TypedTurnStillCarriesFarMoreThanASpokenOne()
    {
        // Deliberately includes tools the voice set does not carry (food_log, goal_create,
        // alert_create) — with a catalogue made only of voice tools the two paths keep the
        // same count, and the assertion would prove nothing.
        var all = Catalogue([.. All, "food_log", "goal_create", "alert_create", "memory_save"]);
        Assert.True(ChatTools.Filter(all).Count > VoiceTools.Filter(all).Count);
    }
}
