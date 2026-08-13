using Maaya.Auth;

namespace Maaya.Auth.Tests;

// The bug these pin down cost every password login since the system was built, and
// produced no symptom beyond "invalid credentials" — indistinguishable from typing the
// wrong password. The hash was written with doubled dollars for a compose
// `environment:` block and then delivered via `env_file:`, which passes values through
// literally, so bcrypt received 63 characters that were never a hash.
public class AuthSecretsTests
{
    // The shape of the real deployed value, with the digest body replaced.
    private const string Good = "$2a$12$t4s1RkHbg/wo5AXoUbSnS.cMqPvDuQuriLuv5eaTYKtaqDGlcfRsq";
    private const string Doubled = "$$2a$$12$$t4s1RkHbg/wo5AXoUbSnS.cMqPvDuQuriLuv5eaTYKtaqDGlcfRsq";

    [Fact]
    public void RealHashIsSixtyCharsStartingDollarTwo()
    {
        Assert.Equal(60, Good.Length);
        Assert.True(AuthSecrets.LooksLikeBcrypt(Good));
    }

    [Fact]
    public void DoubledHashIsNotUsableAsIs()
    {
        Assert.Equal(63, Doubled.Length);
        Assert.False(AuthSecrets.LooksLikeBcrypt(Doubled));
    }

    [Fact]
    public void NormalizeRepairsTheDoubledHash()
        => Assert.Equal(Good, AuthSecrets.Normalize(Doubled));

    // The repair must be invisible to a correctly-written env file — otherwise fixing
    // vault.env would break the thing the fix was for.
    [Fact]
    public void NormalizeLeavesACorrectHashAlone()
        => Assert.Equal(Good, AuthSecrets.Normalize(Good));

    // A genuinely corrupt value must stay corrupt rather than be "repaired" into some
    // other string that then silently fails to verify against any password.
    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$2a$12$tooshort")]
    [InlineData("$$$$$$")]
    public void NormalizeDoesNotInventAHash(string raw)
    {
        var result = AuthSecrets.Normalize(raw);
        Assert.Equal(raw, result);
        Assert.False(AuthSecrets.LooksLikeBcrypt(result));
    }

    // Drives the boot diagnostic. Reporting "repaired" for a hash that was already fine
    // would send someone editing a file that has nothing wrong with it.
    [Fact]
    public void NeededUndoublingOnlyTrueForTheRepairableCase()
    {
        Assert.True(AuthSecrets.NeededUndoubling(Doubled));
        Assert.False(AuthSecrets.NeededUndoubling(Good));
        Assert.False(AuthSecrets.NeededUndoubling("garbage"));
    }

    // bcrypt has emitted several version prefixes over the years; all are 60 chars and
    // all begin "$2". Rejecting $2b/$2y would lock out anyone whose hash came from a
    // different tool than the one that made this one.
    [Theory]
    [InlineData("$2a$")]
    [InlineData("$2b$")]
    [InlineData("$2y$")]
    public void AllBcryptVersionPrefixesAreAccepted(string prefix)
    {
        var hash = prefix + new string('x', 60 - prefix.Length);
        Assert.True(AuthSecrets.LooksLikeBcrypt(hash));
    }

    [Fact]
    public void NullHashIsNotBcrypt() => Assert.False(AuthSecrets.LooksLikeBcrypt(null));
}
