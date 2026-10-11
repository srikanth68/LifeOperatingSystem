using San.Application;

namespace San.Tests;

public class MemoryDistillationTests
{
    [Fact]
    public void ReadsKindTextAndNames()
    {
        var l = Assert.Single(MemoryDistillation.Parse("fact|Drives a blue Honda Civic|Honda Civic"));

        Assert.Equal("fact", l.Kind);
        Assert.Equal("Drives a blue Honda Civic", l.Text);
        Assert.Equal(["Honda Civic"], l.Entities);
    }

    // The old two-field format must keep working: a model that ignores the new field
    // still produces memories.
    [Fact]
    public void NamesAreOptional()
    {
        var l = Assert.Single(MemoryDistillation.Parse("preference|Prefers aisle seats"));

        Assert.Empty(l.Entities);
    }

    // "fact" is the kind NorthStar used to refuse. It must reach it as a fact, not be
    // folded into something else.
    [Fact]
    public void FactStaysAFact() =>
        Assert.Equal("fact", MemoryDistillation.Parse("fact|Blood group is O positive")[0].Kind);

    [Fact]
    public void AnUnknownKindBecomesAnObservation() =>
        Assert.Equal("observation", MemoryDistillation.Parse("habit|Runs every morning")[0].Kind);

    [Fact]
    public void TheUserAndSanAreNeverEntities()
    {
        var l = Assert.Single(MemoryDistillation.Parse("decision|Will refinance in 2027|User; San; Hyderabad flat"));

        Assert.Equal(["Hyderabad flat"], l.Entities);
    }

    [Fact]
    public void LinesThatAreNotMemoriesAreSkipped()
    {
        var lines = MemoryDistillation.Parse("Here are the memories:\nevent|Moved to Hyderabad on 2026-06-01|Hyderabad\n|nothing\nfact|x");

        Assert.Equal("Moved to Hyderabad on 2026-06-01", Assert.Single(lines).Text);
    }

    [Fact]
    public void EvidenceIsTheUsersWordsOnly()
    {
        var e = MemoryDistillation.Evidence([
            ("user", "my blood group is O+"),
            ("assistant", "Noted, your blood group is O positive."),
            ("user", "and I'm   allergic to penicillin"),
        ]);

        Assert.Equal("my blood group is O+ / and I'm allergic to penicillin", e);
    }

    // The end of a long window is what the model just read, so that is what is kept.
    [Fact]
    public void LongEvidenceKeepsTheEnd()
    {
        var e = MemoryDistillation.Evidence([("user", new string('a', 900) + " the important end")], max: 100);

        Assert.Equal(100, e.Length);
        Assert.EndsWith("the important end", e);
    }

    [Fact]
    public void SourceRefPointsAtTheChatWindow()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Equal($"san:chat:{a}..{b}", MemoryDistillation.SourceRef(a, b));
        Assert.Equal($"san:chat:{a}", MemoryDistillation.SourceRef(a, a));
    }
}

public class RecalledMemoriesTests
{
    private static RecalledMemories.Item Item(string content, string? status) =>
        new(content, "preference", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), status);

    [Fact]
    public void AnInferredMemoryIsLabelledAndTheModelToldWhatThatMeans()
    {
        var block = RecalledMemories.Format([Item("Prefers oat milk", "proposed")])!;

        Assert.Contains("- (preference, 2026-08-01, unconfirmed) Prefers oat milk", block);
        Assert.Contains("do not state it to them as fact", block);
    }

    [Fact]
    public void AConfirmedMemoryCarriesNoLabelAndTheHeaderStaysShort()
    {
        var block = RecalledMemories.Format([Item("Prefers oat milk", "active")])!;

        Assert.Contains("- (preference, 2026-08-01) Prefers oat milk", block);
        Assert.DoesNotContain("unconfirmed", block);
    }

    // A NorthStar that predates statuses sends none; those memories were all in use as fact.
    [Fact]
    public void NoStatusMeansConfirmed() =>
        Assert.DoesNotContain("unconfirmed", RecalledMemories.Format([Item("Likes jazz", null)])!);

    [Fact]
    public void NothingToShowIsNull() =>
        Assert.Null(RecalledMemories.Format([Item("  ", "active")]));
}
