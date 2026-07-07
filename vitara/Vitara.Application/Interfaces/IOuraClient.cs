using Vitara.Domain.Entities;

namespace Vitara.Application.Interfaces;

public interface IOuraClient
{
    Task<string> ExchangeCodeAsync(string code);
    Task<string> RefreshAccessTokenAsync(string refreshToken);

    Task<UserProfile> GetPersonalInfoAsync(string accessToken);
    Task<List<SleepSession>> GetSleepAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailyReadiness>> GetReadinessAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailyActivity>> GetActivityAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailyStress>> GetStressAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailyResilience>> GetResilienceAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailyCardiovascularAge>> GetCardiovascularAgeAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<DailySpo2>> GetSpo2Async(string accessToken, DateOnly from, DateOnly to);
    Task<List<HeartRateSample>> GetHeartRateAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<Vo2MaxRecord>> GetVo2MaxAsync(string accessToken, DateOnly from, DateOnly to);
    Task<List<Workout>> GetWorkoutsAsync(string accessToken, DateOnly from, DateOnly to);
}
