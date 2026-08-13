using System.Net;
using System.Net.Sockets;

namespace Maaya.Auth;

// KNOWN LIMITATION — this currently trusts more than it looks like it does.
//
// Every browser request reaches a module through the frontend's nginx, so the
// RemoteIpAddress a controller sees is nginx's own address on the Docker bridge
// (172.x), not the caller's. That lands inside the always-trusted 172.16.0.0/12 range
// below, so IsTrusted returns true for any proxied request and AUTH_TRUSTED_NETWORKS
// never gets consulted. nginx does set X-Forwarded-For (see nginx-locations.conf), but
// nothing consumes it: AddMaayaAuth installs no ForwardedHeaders middleware.
//
// The endpoint that made this dangerous — POST /api/auth/auto, tokens for network
// trust alone — has been removed, so today the worst case is that the PIN pad is
// offered to a caller who shouldn't see it. They still need the PIN.
//
// Fixing it properly means UseForwardedHeaders with KnownProxies limited to the
// frontend container, which changes who can log in and therefore wants testing from
// both the Mac and Meshnet before it ships. Until then, treat the PIN as the real
// boundary and don't add anything else gated on IsTrusted alone.
public static class TrustedNetwork
{
    private static readonly Lazy<List<(IPAddress Network, int Prefix)>> Cidrs = new(ParseConfig);

    public static bool IsTrusted(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        // IPv6-mapped IPv4 (::ffff:127.0.0.1) → unwrap
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return true;

        foreach (var (network, prefix) in Cidrs.Value)
        {
            if (IsInSubnet(ip, network, prefix))
                return true;
        }

        return false;
    }

    private static List<(IPAddress, int)> ParseConfig()
    {
        var raw = Environment.GetEnvironmentVariable("AUTH_TRUSTED_NETWORKS") ?? "";
        var result = new List<(IPAddress, int)>();

        // Always trust private ranges: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
        result.Add((IPAddress.Parse("10.0.0.0"), 8));
        result.Add((IPAddress.Parse("172.16.0.0"), 12));
        result.Add((IPAddress.Parse("192.168.0.0"), 16));

        foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('/');
            if (IPAddress.TryParse(parts[0], out var addr))
            {
                var prefix = parts.Length > 1 && int.TryParse(parts[1], out var p)
                    ? p
                    : (addr.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
                result.Add((addr, prefix));
            }
        }

        return result;
    }

    private static bool IsInSubnet(IPAddress address, IPAddress network, int prefixLength)
    {
        if (address.AddressFamily != network.AddressFamily) return false;

        var addrBytes = address.GetAddressBytes();
        var netBytes = network.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        var remainBits = prefixLength % 8;

        for (int i = 0; i < fullBytes; i++)
            if (addrBytes[i] != netBytes[i]) return false;

        if (remainBits > 0 && fullBytes < addrBytes.Length)
        {
            var mask = (byte)(0xFF << (8 - remainBits));
            if ((addrBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }
}
