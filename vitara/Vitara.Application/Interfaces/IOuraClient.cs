using Vitara.Domain.Entities;

namespace Vitara.Application.Interfaces;

public interface IOuraClient
{
    Task<string> ExchangeCodeAsync(string code);
    Task<string> RefreshAccessTokenAsync(string refreshToken);
    Task<List<SleepSession>> GetSleepAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailyReadiness>> GetReadinessAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailyActivity>> GetActivityAsync(string accessToken, DateOnly from, DateOnly to);
}
