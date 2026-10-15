using Vitara.Domain.Entities;

namespace Vitara.Application.Interfaces;

// WHOOP's developer API v2 (Vitara ADR 0029), read-only.
public interface IWhoopClient
{
    // Where to send the person to approve access. `state` comes back on the callback.
    string AuthorizeUrl(string state);
    Task<WhoopTokens> ExchangeCodeAsync(string code);
    Task<WhoopTokens> RefreshAsync(string refreshToken);

    // Everything between two instants, already in Vitara's own shapes.
    Task<WhoopBundle> FetchAsync(string accessToken, DateTimeOffset from, DateTimeOffset to);
}

public record WhoopTokens(string AccessToken, string RefreshToken, int ExpiresIn);

// Raw signals only: WHOOP's own scores are not relabelled into Vitara's fields (ADR 0003).
public record WhoopBundle(
    List<SleepSession> Sleep,
    List<Workout> Workouts);
