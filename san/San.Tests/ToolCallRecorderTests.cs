using System.Text.Json;
using San.Application;
using San.Application.Interfaces;

namespace San.Tests;

// The recorder sits in the path of every tool call San makes. The thing it must never
// do is change what happens: a turn that worked before it existed has to work exactly
// the same way after, whatever the recorder makes of it.
public class ToolCallRecorderTests
{
    private static ToolCall Call(string name, params (string Key, string Value)[] args)
        => new(name, args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public async Task PassesTheResultStraightBack()
    {
        var r = new ToolCallRecorder();
        var wrapped = r.Wrap((_, _) => Task.FromResult("the real result"));

        Assert.Equal("the real result", await wrapped(Call("reminders_list"), default));
    }

    [Fact]
    public async Task RecordsWhatWasCalledAndWithWhat()
    {
        var r = new ToolCallRecorder();
        var wrapped = r.Wrap((_, _) => Task.FromResult("ok"));

        await wrapped(Call("reminder_create", ("text", "call the plumber"), ("dueOn", "2026-09-06")), default);

        var rec = Assert.Single(r.Calls);
        Assert.Equal("reminder_create", rec.Name);
        Assert.True(rec.Ok);
        Assert.Contains("call the plumber", rec.Arguments);
        Assert.Contains("2026-09-06", rec.Arguments);
    }

    [Fact]
    public async Task KeepsCallsInTheOrderTheyHappened()
    {
        var r = new ToolCallRecorder();
        var wrapped = r.Wrap((_, _) => Task.FromResult("ok"));

        await wrapped(Call("agenda_now"), default);
        await wrapped(Call("reminder_create"), default);
        await wrapped(Call("reminder_complete"), default);

        Assert.Equal(["agenda_now", "reminder_create", "reminder_complete"], r.Names);
    }

    [Fact]
    public async Task AFailingToolIsRecordedAndTheExceptionStillPropagates()
    {
        // A tool that throws is often exactly why the reply that follows is wrong, so it
        // is worth recording -- but swallowing it here would change the agent loop's
        // behaviour, which is the one thing this must not do.
        var r = new ToolCallRecorder();
        var wrapped = r.Wrap((_, _) => throw new InvalidOperationException("aasthi returned 500"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => wrapped(Call("property_task_create"), default));

        var rec = Assert.Single(r.Calls);
        Assert.False(rec.Ok);
        Assert.Contains("500", rec.Result);
    }

    [Fact]
    public async Task ClipsAHugeResultInsteadOfStoringIt()
    {
        // A transaction list is megabytes. What a fine-tune learns from is which tool was
        // called with what, not the payload that came back -- and a dataset that is
        // mostly Vault JSON teaches nothing.
        var r = new ToolCallRecorder();
        var wrapped = r.Wrap((_, _) => Task.FromResult(new string('x', 50_000)));

        var returned = await wrapped(Call("vault_transactions"), default);

        Assert.Equal(50_000, returned.Length);          // the caller still gets everything
        Assert.True(r.Calls[0].Result.Length < 1_000);  // the record does not
    }

    [Fact]
    public async Task NamesFeedStraightIntoTheWriteClaimGuard()
    {
        // The point of capturing names in this shape: a turn's guard verdict can be
        // recomputed after the fact, outside the provider that originally judged it.
        var r = new ToolCallRecorder();
        var wrapped = r.Wrap((_, _) => Task.FromResult("ok"));
        await wrapped(Call("reminder_create"), default);

        Assert.False(WriteClaimCheck.ClaimsUnverifiedWrite("I've created the reminder.", r.Names));

        var empty = new ToolCallRecorder();
        Assert.True(WriteClaimCheck.ClaimsUnverifiedWrite("I've created the reminder.", empty.Names));
    }

    [Fact]
    public async Task SerialisesToValidJsonForStorage()
    {
        var r = new ToolCallRecorder();
        var wrapped = r.Wrap((_, _) => Task.FromResult("done"));
        await wrapped(Call("habit_checkin", ("name", "reading")), default);

        using var doc = JsonDocument.Parse(r.ToJson());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal("habit_checkin", doc.RootElement[0].GetProperty("Name").GetString());
    }

    [Fact]
    public void AnUnusedRecorderIsAnEmptyArrayNotNull()
    {
        // Every turn writes a row, including the many that call nothing at all. Those
        // rows still matter: "asked this, called nothing" is a fact about the model.
        var r = new ToolCallRecorder();
        Assert.Empty(r.Calls);
        Assert.Equal("[]", r.ToJson());
    }
}
