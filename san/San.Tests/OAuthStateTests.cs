using San.Application;

namespace San.Tests;

public class OAuthStateTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void AnIssuedTokenIsAcceptedOnceForItsProvider()
    {
        var states = new OAuthState();
        var token = states.Issue("google");

        Assert.True(states.Consume("google", token));
        Assert.False(states.Consume("google", token)); // a replayed callback URL
    }

    // The attack this closes: a callback carrying a code San never asked for.
    [Fact]
    public void AnUnknownOrMissingTokenIsRefused()
    {
        var states = new OAuthState();

        Assert.False(states.Consume("google", null));
        Assert.False(states.Consume("google", "deadbeef"));
    }

    [Fact]
    public void ATokenIsBoundToItsProvider()
    {
        var states = new OAuthState();
        var token = states.Issue("microsoft");

        Assert.False(states.Consume("google", token));
    }

    [Fact]
    public void ATokenExpires()
    {
        var clock = new Clock(DateTimeOffset.UtcNow);
        var states = new OAuthState(clock);
        var token = states.Issue("google");

        clock.Now += OAuthState.Lifetime + TimeSpan.FromSeconds(1);

        Assert.False(states.Consume("google", token));
    }
}
