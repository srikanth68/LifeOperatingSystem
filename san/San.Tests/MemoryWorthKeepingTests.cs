using San.Application;

namespace San.Tests;

// The rejected cases are verbatim from the user's live NorthStar store. All eight were
// recalled together in front of "add a reminder for tomorrow morning to go to USPS at
// 10am", and San answered "I've set a reminder for you" without calling anything.
public class MemoryWorthKeepingTests
{
    [Theory]
    [InlineData("Reminder set for tomorrow morning at 8am for cricket")]
    [InlineData("Reminder set for tomorrow morning at 11 am called \"Reminder test for claude\"")]
    [InlineData("set reminder to look at NorthStar tomorrow morning")]
    [InlineData("reminder set for today at 4pm to go to the mall")]
    [InlineData("Reminder set for 'GO to Bank' on July 23rd at 6:30 PM")]
    [InlineData("Reminder set for 'Review Nexus alerts' for tomorrow at ten AM.")]
    [InlineData("The user wants a reminder created for tomorrow, Wednesday, July 23, 2026, at 9:00 AM EDT")]
    [InlineData("Set reminders for paying toll bill tomorrow, August ninth, at 12 PM and 2 PM")]
    [InlineData("User decided to set a reminder for 7 PM tonight to download insurance cards")]
    public void RejectsActionRecords(string text)
    {
        Assert.False(MemoryWorthKeeping.Keep(text, out var reason));
        Assert.NotNull(reason);
    }

    [Theory]
    // No absolute date anywhere, so these cannot be read correctly tomorrow.
    [InlineData("Meeting with ECSE team scheduled for 2 PM today.")]
    [InlineData("User decided to check the schedule for tomorrow.")]
    public void RejectsUnanchoredRelativeDates(string text)
        => Assert.False(MemoryWorthKeeping.Keep(text, out _));

    [Theory]
    // The whole point of testing DurableSignal first: a preference ABOUT reminders is
    // exactly the kind of memory worth keeping, and it mentions reminders.
    [InlineData("User prefers reminders in the morning rather than the evening.")]
    [InlineData("User always sets a reminder before a property inspection.")]
    [InlineData("User's timezone is America/New_York.")]
    [InlineData("User is training for a half marathon in October 2026.")]
    [InlineData("User owns two properties in Charlotte, NC.")]
    [InlineData("User works at a software company and codes in C#.")]
    [InlineData("Kanth decided to move Maaya off Whisper and use Gemma for speech to text.")]
    public void KeepsDurableFacts(string text)
    {
        Assert.True(MemoryWorthKeeping.Keep(text, out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void RejectsEmpty()
    {
        Assert.False(MemoryWorthKeeping.Keep("   ", out var reason));
        Assert.Equal("empty", reason);
    }

    [Fact]
    public void KeepsAnEventThatCarriesAnAbsoluteDate()
        => Assert.True(MemoryWorthKeeping.Keep(
            "User's daughter was born on 2019-03-14.", out _));
}
