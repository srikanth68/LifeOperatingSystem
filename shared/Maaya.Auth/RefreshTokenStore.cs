namespace Maaya.Auth;

public sealed class RefreshTokenEntry
{
    public required string Token { get; init; }
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public bool Revoked { get; set; }
}

public sealed class RefreshTokenStore
{
    private readonly Dictionary<string, RefreshTokenEntry> _tokens = new();

    public void Store(RefreshTokenEntry entry) =>
        _tokens[entry.Token] = entry;

    public RefreshTokenEntry? Get(string token) =>
        _tokens.GetValueOrDefault(token);

    public void Revoke(string token)
    {
        if (_tokens.TryGetValue(token, out var entry))
            entry.Revoked = true;
    }

    public void RevokeAll()
    {
        foreach (var entry in _tokens.Values)
            entry.Revoked = true;
    }

    public void Cleanup()
    {
        var expired = _tokens
            .Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow || kv.Value.Revoked)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in expired)
            _tokens.Remove(key);
    }
}
