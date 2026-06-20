using Vitara.Domain.Entities;

namespace Vitara.Application.Interfaces;

public interface IVitaraRepository
{
    // Token
    Task<OuraToken?> GetTokenAsync();
    Task SaveTokenAsync(OuraToken token);
    Task DeleteTokenAsync();

    // Sleep
    Task UpsertSleepAsync(IEnumerable<SleepSession> sessions);
    Task<List<SleepSession>> GetSleepAsync(DateOnly from, DateOnly to);

    // Readiness
    Task UpsertReadinessAsync(IEnumerable<DailyReadiness> records);
    Task<List<DailyReadiness>> GetReadinessAsync(DateOnly from, DateOnly to);

    // Activity
    Task UpsertActivityAsync(IEnumerable<DailyActivity> records);
    Task<List<DailyActivity>> GetActivityAsync(DateOnly from, DateOnly to);
}
