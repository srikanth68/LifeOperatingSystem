using Maaya.Mcp.Tools;

namespace Maaya.Mcp.Tests;

// The resolver exists because of one conversation. Asked to close out the tree
// trimming, San replied "I need the property ID for Scoter Street. Can you provide
// that?" -- to someone who has never seen a GUID. Measured later against the real
// catalogue, that failure reproduced in eight runs out of ten.
//
// The reminder texts below are the ones actually in the database.
public class NameResolverTests
{
    private const string Reminders = """
    [
      {"id":"11111111-1111-1111-1111-111111111111","text":"Arrange Tree trimming at 15128 Scoter Street"},
      {"id":"22222222-2222-2222-2222-222222222222","text":"Pay Spectrum Bill ($79.99)"},
      {"id":"33333333-3333-3333-3333-333333333333","text":"Pay credit card bills"},
      {"id":"44444444-4444-4444-4444-444444444444","text":"Schedule blood test"}
    ]
    """;

    private const string Habits = """
    [
      {"id":"aaaaaaaa-0000-0000-0000-000000000001","name":"Reading"},
      {"id":"aaaaaaaa-0000-0000-0000-000000000002","name":"Meditation"},
      {"id":"aaaaaaaa-0000-0000-0000-000000000003","name":"Morning walk"}
    ]
    """;

    [Fact]
    public void TheTreeTrimmingCaseResolves()
    {
        // The whole point. "tree trimming" finds a reminder whose text is six words
        // longer, without anyone typing a GUID.
        var (id, error) = NameResolver.Resolve("tree trimming", Reminders, "reminder");

        Assert.Null(error);
        Assert.Equal("11111111-1111-1111-1111-111111111111", id);
    }

    [Fact]
    public void SoDoesTheWayAPersonWouldActuallySayIt()
    {
        var (id, _) = NameResolver.Resolve("Tree trimming at Scoter Street", Reminders, "reminder");
        Assert.Equal("11111111-1111-1111-1111-111111111111", id);
    }

    [Fact]
    public void AnIdStillWorksUnchanged()
    {
        // The previous contract has to keep working for any caller that does hold one.
        var (id, error) = NameResolver.Resolve("22222222-2222-2222-2222-222222222222", Reminders, "reminder");

        Assert.Null(error);
        Assert.Equal("22222222-2222-2222-2222-222222222222", id);
    }

    [Fact]
    public void AnExactNameBeatsALongerOneContainingIt()
    {
        var (id, error) = NameResolver.Resolve("Reading", Habits, "habit");

        Assert.Null(error);
        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001", id);
    }

    [Fact]
    public void CaseDoesNotMatter()
        => Assert.Equal("aaaaaaaa-0000-0000-0000-000000000002",
            NameResolver.Resolve("meditation", Habits, "habit").Id);

    [Fact]
    public void AnAmbiguousMatchAsksRatherThanGuesses()
    {
        // Two reminders mention paying a bill. Picking one would tick off the wrong
        // obligation silently, which is the failure the settlement matcher was built
        // to avoid -- a wrongly-closed bill reminder is a missed payment.
        var (id, error) = NameResolver.Resolve("Pay", Reminders, "reminder");

        Assert.Null(id);
        Assert.Contains("matches several", error);
        Assert.Contains("Spectrum", error);
    }

    [Fact]
    public void NoMatchListsWhatIsActuallyThere()
    {
        // Far more useful than "not found": the user can see what they meant.
        var (id, error) = NameResolver.Resolve("dentist", Reminders, "reminder");

        Assert.Null(id);
        Assert.Contains("Schedule blood test", error);
    }

    [Fact]
    public void PartialWordsDoNotMatchAcrossDifferentThings()
    {
        // "call the plumber" must never resolve to "call the dentist". Every
        // meaningful word has to appear, not just any one of them.
        const string json = """
        [
          {"id":"55555555-5555-5555-5555-555555555555","text":"Call the dentist"}
        ]
        """;

        Assert.Null(NameResolver.Resolve("call the plumber", json, "reminder").Id);
    }

    [Fact]
    public void ShortWordsAreIgnoredWhenMatching()
    {
        // "the", "at" and similar would otherwise make almost anything match almost
        // anything else.
        var (id, _) = NameResolver.Resolve("the tree trimming at scoter", Reminders, "reminder");
        Assert.Equal("11111111-1111-1111-1111-111111111111", id);
    }

    [Fact]
    public void AWrappedArrayIsFoundToo()
    {
        // karma_habits returns {"habitsToday":[...],"goals":[...]} rather than a bare
        // array, and every caller should not have to know which shape it gets.
        const string wrapped = """
        {"habitsToday":[{"id":"aaaaaaaa-0000-0000-0000-000000000001","name":"Reading"}]}
        """;

        Assert.Equal("aaaaaaaa-0000-0000-0000-000000000001",
            NameResolver.Resolve("reading", wrapped, "habit").Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyTermIsRejected(string term)
        => Assert.NotNull(NameResolver.Resolve(term, Reminders, "reminder").Error);

    [Fact]
    public void AnEmptyListSaysSoRatherThanFailingObscurely()
    {
        var (id, error) = NameResolver.Resolve("anything", "[]", "reminder");

        Assert.Null(id);
        Assert.Contains("no open reminder", error);
    }

    [Fact]
    public void MalformedJsonIsReportedNotThrown()
    {
        // The module could be mid-restart. A resolver that throws takes the turn with
        // it; one that reports lets the model say something useful.
        var (id, error) = NameResolver.Resolve("anything", "not json", "reminder");

        Assert.Null(id);
        Assert.NotNull(error);
    }
}
