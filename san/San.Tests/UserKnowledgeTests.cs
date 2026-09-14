using San.Application;

namespace San.Tests;

// The facts block sits in the system prompt, inside llama.cpp's cached prefix, so the
// property that matters most is that it renders identically on every turn.
public class UserKnowledgeTests
{
    [Fact]
    public void FactsRenderInAStableOrderWhateverOrderTheyArrive()
    {
        var a = UserKnowledge.FactsBlock([("partner_name", "Asha"), ("car", "Blue Honda Civic"), ("Allergies", "Peanuts")]);
        var b = UserKnowledge.FactsBlock([("car", "Blue Honda Civic"), ("Allergies", "Peanuts"), ("partner_name", "Asha")]);

        Assert.Equal(a, b);
        Assert.Contains("- partner name: Asha", a);
    }

    [Fact]
    public void TimezoneIsLeftToTheTimeContext()
    {
        var block = UserKnowledge.FactsBlock([("timezone", "America/New_York"), ("car", "Civic")]);

        Assert.DoesNotContain("America/New_York", block);
        Assert.Contains("car: Civic", block);
    }

    [Fact]
    public void NoFactsMeansNoBlock()
    {
        Assert.Null(UserKnowledge.FactsBlock([]));
        Assert.Null(UserKnowledge.FactsBlock([("timezone", "UTC"), ("empty", " ")]));
    }

    [Fact]
    public void ALongFactIsClipped()
    {
        var block = UserKnowledge.FactsBlock([("notes", new string('x', 500))])!;
        Assert.True(block.Length < 500);
        Assert.EndsWith("…", block);
    }

    [Fact]
    public void InsightsAreNewestFirstDatedAndCapped()
    {
        var block = UserKnowledge.InsightsBlock(
        [
            ("Old pattern", "Spends more on weekends", new DateTime(2026, 6, 1)),
            ("Newest pattern", "Sleeps less before travel", new DateTime(2026, 9, 10)),
            ("Middle pattern", "Skips the gym on Mondays", new DateTime(2026, 8, 1)),
            ("Oldest pattern", "Orders takeout after late meetings", new DateTime(2026, 5, 1)),
        ], limit: 3)!;

        var lines = block.Split('\n').Skip(1).ToList();
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("- (2026-09-10) Newest pattern", lines[0]);
        Assert.DoesNotContain("Oldest pattern", block);
    }

    [Fact]
    public void NoInsightsMeansNoBlock() => Assert.Null(UserKnowledge.InsightsBlock([]));
}
