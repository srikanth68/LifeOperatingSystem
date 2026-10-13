using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace San.Application;

// The OAuth "state" check for connecting a mailbox.
//
// The callback is anonymous by necessity (Google and Microsoft redirect the browser
// there, with no way to carry San's auth header), and it used to accept any code it
// was handed. So a link built from someone else's consent could connect THEIR mailbox
// to this San, which would then read their mail into this user's reminders and memory.
// Now the connect step issues a single-use, short-lived token that only this server
// knows, the provider hands it back unchanged, and a callback without a live one is
// refused.
//
// In memory on purpose: a restart between clicking "Connect" and finishing consent
// costs one retry, and nothing about a half-finished connect needs to survive it.
public class OAuthState(TimeProvider? clock = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (string Provider, DateTimeOffset Expires)> pending = new();

    public string Issue(string provider)
    {
        Sweep();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        pending[token] = (provider, clock.GetUtcNow() + Lifetime);
        return token;
    }

    // Single use: a token is removed whether or not it matches, so a leaked callback
    // URL cannot be replayed.
    public bool Consume(string provider, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !pending.TryRemove(token, out var entry)) return false;
        return entry.Provider == provider && clock.GetUtcNow() <= entry.Expires;
    }

    private void Sweep()
    {
        var now = clock.GetUtcNow();
        foreach (var (k, v) in pending)
            if (v.Expires < now) pending.TryRemove(k, out _);
    }
}
