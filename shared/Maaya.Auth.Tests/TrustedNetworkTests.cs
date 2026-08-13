using System.Net;
using Maaya.Auth;

namespace Maaya.Auth.Tests;

// Deliberately exercises only the behaviour that does NOT depend on
// AUTH_TRUSTED_NETWORKS: TrustedNetwork caches its parsed CIDR list in a Lazy on first
// use, so a test that set the variable would leak into whichever test happened to run
// second. The always-on private ranges are the interesting part anyway — they're what
// makes every proxied request look trusted.
public class TrustedNetworkTests
{
    private static bool Trusted(string ip) => TrustedNetwork.IsTrusted(IPAddress.Parse(ip));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void LoopbackIsTrusted(string ip) => Assert.True(Trusted(ip));

    // The IPv6-mapped form is what Kestrel actually hands you on a dual-stack socket,
    // so an unwrapping bug here would reject the local machine.
    [Fact]
    public void IPv4MappedLoopbackIsTrusted() => Assert.True(Trusted("::ffff:127.0.0.1"));

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.20")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    public void PrivateRangesAreTrusted(string ip) => Assert.True(Trusted(ip));

    // This is the one that matters most: Docker's default bridge networks live in
    // 172.17-172.31, so a request arriving from the nginx container is indistinguishable
    // from a request off the LAN. Every proxied call therefore reads as trusted, which
    // is why the PIN is the real boundary and POST /api/auth/auto had to be removed.
    [Fact]
    public void DockerBridgeAddressesAreTrusted()
    {
        Assert.True(Trusted("172.17.0.2"));
        Assert.True(Trusted("172.18.0.7"));
    }

    // 172.16.0.0/12 covers 172.16-172.31 only. Treating the whole 172.x block as
    // private would wrongly trust real routable addresses.
    [Theory]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    public void AddressesOutsideThe172SlashTwelveAreNotTrusted(string ip) => Assert.False(Trusted(ip));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("203.0.113.9")]
    [InlineData("11.0.0.1")]
    public void PublicAddressesAreNotTrusted(string ip) => Assert.False(Trusted(ip));
}
