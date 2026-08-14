using San.Application;

namespace San.Tests;

// Runs on every single chat turn and fails silently by design: if it trims too hard,
// San simply answers with less context and nobody sees a error. Uses the default
// budget (3000 tokens / 6 messages) throughout — CHAT_HISTORY_* are read from
// process-wide environment, so setting them would leak across tests.
public class ChatWindowTests
{
    private record Msg(string Role, string Content);

    private static List<Msg> Conversation(int count, int charsEach)
    {
        var body = new string('x', charsEach);
        return Enumerable.Range(0, count)
            .Select(i => new Msg(i % 2 == 0 ? "user" : "assistant", $"{i}:{body}"))
            .ToList();
    }

    private static List<Msg> Select(List<Msg> history) =>
        ChatWindow.Select(history, m => m.Content, m => m.Role);

    [Fact]
    public void EmptyHistoryStaysEmpty()
        => Assert.Empty(Select([]));

    [Fact]
    public void EstimateTokensIsAboutFourCharsEach()
    {
        Assert.Equal(0, ChatWindow.EstimateTokens(""));
        Assert.Equal(0, ChatWindow.EstimateTokens(null));
        Assert.Equal(26, ChatWindow.EstimateTokens(new string('x', 100)));
    }

    [Fact]
    public void AShortConversationIsKeptWhole()
    {
        var history = Conversation(8, 40);
        Assert.Equal(8, Select(history).Count);
    }

    // The floor exists so one pasted stack trace can't erase the conversation around
    // it. Six messages of 1000 tokens each is 6000 against a 3000 budget, and they are
    // kept regardless.
    [Fact]
    public void MinimumMessagesSurviveEvenWhenWayOverBudget()
    {
        var history = Conversation(20, 4000);
        var kept = Select(history);
        Assert.True(kept.Count >= 5, $"expected the floor to hold, got {kept.Count}");
        Assert.True(kept.Sum(m => ChatWindow.EstimateTokens(m.Content)) > ChatWindow.TokenBudget);
    }

    // What the class is actually for: bounded by size, not by message count.
    [Fact]
    public void LongConversationIsTrimmedToTheBudget()
    {
        var history = Conversation(20, 1600);
        var kept = Select(history);

        Assert.True(kept.Count < history.Count, "expected trimming");
        Assert.EndsWith(history[^1].Content, kept[^1].Content);   // newest is always kept
    }

    // Dropping from the wrong end would hand the model an old conversation and hide the
    // question just asked.
    [Fact]
    public void TrimmingDropsTheOldestNotTheNewest()
    {
        var history = Conversation(20, 1600);
        var kept = Select(history);
        Assert.Same(history[^1], kept[^1]);
        Assert.DoesNotContain(history[0], kept);
    }

    // An assistant reply with no visible question reads as though San said it
    // unprompted, and the model tries to interpret it as context rather than an answer.
    [Fact]
    public void WindowNeverOpensOnAnAssistantReply()
    {
        var kept = Select(Conversation(20, 1600));
        Assert.Equal("user", kept[0].Role);
    }

    [Fact]
    public void WindowStillOpensOnUserWhenHistoryStartsWithAssistant()
    {
        List<Msg> history = [
            new("assistant", "unprompted"),
            new("user", "hello"),
            new("assistant", "hi"),
        ];
        var kept = Select(history);
        Assert.Equal("user", kept[0].Role);
        Assert.Equal(2, kept.Count);
    }

    // A single message is kept even if it's an assistant turn — the loop stops at one,
    // because returning nothing would be worse than returning an orphan reply.
    [Fact]
    public void ASoleAssistantMessageIsNotDiscarded()
    {
        var kept = Select([new Msg("assistant", "only thing ever said")]);
        Assert.Single(kept);
    }

    // The single biggest lever on voice latency. Measured against the live server at
    // San's real prompt size: 78 messages of history cost 17-48s per spoken turn, 24
    // cost 5-6s, 12 cost 3.5-5.5s. The window slides either way — a small one just
    // leaves little behind the divergence point to re-read.
    [Fact]
    public void VoiceBudgetKeepsFarLessHistory()
    {
        var history = Conversation(60, 160);
        var typed = Select(history);
        var spoken = ChatWindow.Select(history, m => m.Content, m => m.Role, ChatWindow.VoiceTokenBudget);

        Assert.True(spoken.Count < typed.Count,
            $"voice window ({spoken.Count}) should be smaller than typed ({typed.Count})");
        Assert.True(spoken.Sum(m => ChatWindow.EstimateTokens(m.Content)) <= ChatWindow.VoiceTokenBudget + 200);
    }

    // Smaller must not mean broken: the floor and the open-on-a-user-turn rule still hold.
    [Fact]
    public void VoiceWindowStillObeysTheStructuralRules()
    {
        var spoken = ChatWindow.Select(Conversation(60, 160), m => m.Content, m => m.Role,
            ChatWindow.VoiceTokenBudget);
        Assert.Equal("user", spoken[0].Role);
        Assert.True(spoken.Count >= 5, $"expected the floor to hold, got {spoken.Count}");
    }

    [Fact]
    public void AnExplicitBudgetOverridesTheDefault()
    {
        var history = Conversation(60, 160);
        var tiny = ChatWindow.Select(history, m => m.Content, m => m.Role, 50);
        var big = ChatWindow.Select(history, m => m.Content, m => m.Role, 100_000);
        Assert.True(tiny.Count < big.Count);
        Assert.Equal(history.Count, big.Count);
    }

    [Fact]
    public void RoleMatchingIgnoresCase()
    {
        List<Msg> history = [new("ASSISTANT", "a"), new("User", "b"), new("assistant", "c")];
        Assert.Equal("User", Select(history)[0].Role);
    }
}
