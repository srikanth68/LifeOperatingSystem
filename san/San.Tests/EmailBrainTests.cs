using System.Text.Json;
using San.Application;

namespace San.Tests;

public class EmailTextTests
{
    [Fact]
    public void HtmlBecomesReadableText()
    {
        var html = "<html><style>p{color:red}</style><body><p>Your bill of <b>$79.99</b> is due&nbsp;Oct 20.</p><p>Thanks</p></body></html>";

        var text = EmailText.Clean(EmailText.ToPlainText(html, isHtml: true));

        Assert.Contains("Your bill of $79.99 is due Oct 20.", text);
        Assert.Contains("Thanks", text);
        Assert.DoesNotContain("color:red", text);
        Assert.DoesNotContain("<", text);
    }

    // The thread below a reply is not read again: NorthStar already holds the earlier
    // messages and shows them beside this one.
    [Fact]
    public void QuotedReplyAndSignatureAreCut()
    {
        var body = "Sounds good, Thursday works.\n\nOn Tue, 14 Oct 2026 at 10:02, Priya <p@x.com> wrote:\n> Can we meet Thursday?\n> Thanks";
        var signed = "See you then.\n-- \nRavi | Acme Corp | +1 555 0100";

        Assert.Equal("Sounds good, Thursday works.", EmailText.Clean(body));
        Assert.Equal("See you then.", EmailText.Clean(signed));
    }

    [Fact]
    public void OutlookOriginalMessageBlockIsCut()
    {
        var body = "Approved.\n\n-----Original Message-----\nFrom: Finance\nPlease approve the invoice.";

        Assert.Equal("Approved.", EmailText.Clean(body));
    }

    [Fact]
    public void LongMailIsCutAndSaysSo()
    {
        var text = EmailText.Clean(new string('a', 10_000), max: 100);

        Assert.StartsWith(new string('a', 100), text);
        Assert.Contains("cut", text);
    }
}

public class EmailSenderTests
{
    [Theory]
    [InlineData("Acme Billing <Billing@Acme.com>", "Acme Billing", "billing@acme.com")]
    [InlineData("\"Priya S\" <p@x.com>", "Priya S", "p@x.com")]
    [InlineData("noreply@bank.com", null, "noreply@bank.com")]
    [InlineData("<only@addr.com>", null, "only@addr.com")]
    public void SplitsNameAndAddress(string from, string? name, string? address)
    {
        var (n, a) = EmailSender.Parse(from);

        Assert.Equal(name, n);
        Assert.Equal(address, a);
    }
}

public class EmailNotesTests
{
    [Fact]
    public void ReadsEverythingTheContractAsksFor()
    {
        var id = Guid.NewGuid();
        var notes = EmailNotes.Parse($$"""
            {"importance":"act","summary":"Spectrum internet bill of $79.99 due 2026-10-20.",
             "findings":[{"key":"bill.spectrum.2026-10-20","severity":"low","message":"Spectrum bill $79.99 due Oct 20."}],
             "remember":[{"kind":"fact","text":"Spectrum account number ends 4421","about":["Spectrum"]}],
             "corrects":[{"memory":"{{id}}","text":"Internet provider is Spectrum, not AT&T"}]}
            """);

        Assert.Equal(EmailNotes.Act, notes.Importance);
        Assert.Contains("79.99", notes.Summary);
        var r = Assert.Single(notes.Remember);
        Assert.Equal(["Spectrum"], r.About);
        Assert.Equal(id, Assert.Single(notes.Corrects).Memory);
    }

    [Fact]
    public void TheSentinelMeansIgnore()
    {
        var notes = EmailNotes.Parse(FindingParser.NothingImportant);

        Assert.False(notes.Keep);
    }

    // A small model's prose instead of JSON must not make an email vanish: it is
    // recorded as having arrived, with nothing learned from it.
    [Fact]
    public void UnreadableReplyStillKeepsTheEmail()
    {
        var notes = EmailNotes.Parse("I looked at this email and it seems to be about a bill.");

        Assert.True(notes.Keep);
        Assert.Null(notes.Summary);
        Assert.Empty(notes.Remember);
    }

    [Fact]
    public void BadIdsAndTinyTextsAreDropped()
    {
        var notes = EmailNotes.Parse("""
            {"importance":"note","summary":"x","remember":[{"kind":"weird","text":"ok"},{"text":"Lives in Hyderabad","about":"Hyderabad; "}],
             "corrects":[{"memory":"not-a-guid","text":"something"}]}
            """);

        var r = Assert.Single(notes.Remember);
        Assert.Equal("fact", r.Kind);
        Assert.Equal(["Hyderabad"], r.About);
        Assert.Empty(notes.Corrects);
    }
}

public class EmailBrainPromptTests
{
    private static JsonElement Related(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void RelatedKnowledgeIsLaidOutWithMemoryIdsAndDates()
    {
        var id = Guid.NewGuid();
        var related = Related($$"""
            {"entities":[{"name":"Hyderabad flat","type":"property","linked":[{"summary":"Rent due on the 5th"}]}],
             "memories":[{"id":"{{id}}","content":"Internet provider is AT&T","kind":"fact","status":"proposed","createdAt":"2026-08-01T00:00:00Z"}],
             "earlier":[{"title":"Spectrum welcome email","occurredAt":"2026-09-30T10:00:00Z"}]}
            """);

        var block = EmailBrainPrompt.Related(related)!;

        Assert.Contains($"memory {id} (fact, saved 2026-08-01, unconfirmed): Internet provider is AT&T", block);
        Assert.Contains("earlier email (2026-09-30): Spectrum welcome email", block);
        Assert.Contains("property \"Hyderabad flat\": Rent due on the 5th", block);
        Assert.Equal(["Hyderabad flat"], EmailBrainPrompt.RelatedEntityNames(related));
        Assert.Equal("fact", EmailBrainPrompt.RelatedMemoryKinds(related)[id]);
    }

    [Fact]
    public void NothingRelatedSaysSo()
    {
        Assert.Null(EmailBrainPrompt.Related(Related("""{"entities":[],"memories":[],"earlier":[]}""")));
        Assert.Contains("nothing related",
            EmailBrainPrompt.UserTurn("me@x.com", "a@b.com", "Hi", DateTime.UtcNow, "body", null));
    }

    [Fact]
    public void TheEventDetailCarriesSenderAndThreadForTheNextEmailToFind()
    {
        Assert.Equal("from billing@acme.com · thread t-9 · Invoice",
            EmailBrainPrompt.Detail("billing@acme.com", "t-9", "Invoice"));
        Assert.Equal("email:google:abc", EmailBrainPrompt.EventKey("google", "abc"));
    }

    // The output contract is fixed in code: a prompt edited in Settings cannot
    // remove the fields the worker parses.
    [Fact]
    public void TheContractIsNotPartOfTheEditablePrompt()
    {
        Assert.DoesNotContain("\"remember\"", EmailTriageDefaults.Prompt);
        Assert.Contains("\"remember\"", EmailTriageDefaults.OutputContract);
        Assert.Contains("\"corrects\"", EmailTriageDefaults.OutputContract);
        Assert.Contains("memory_save", EmailTriageDefaults.WithheldTools);
    }
}
