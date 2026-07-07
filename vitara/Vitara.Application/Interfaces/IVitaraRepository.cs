using Vitara.Domain.Entities;

namespace Vitara.Application.Interfaces;

public interface IVitaraRepository
{
    // Token
    Task<OuraToken?> GetTokenAsync();
    Task SaveTokenAsync(OuraToken token);
    Task DeleteTokenAsync();

    // Profile
    Task<UserProfile?> GetProfileAsync();
    Task SaveProfileAsync(UserProfile profile);

    // Sleep
    Task UpsertSleepAsync(IEnumerable<SleepSession> sessions);
    Task<List<SleepSession>> GetSleepAsync(DateOnly from, DateOnly to);

    // Readiness
    Task UpsertReadinessAsync(IEnumerable<DailyReadiness> records);
    Task<List<DailyReadiness>> GetReadinessAsync(DateOnly from, DateOnly to);

    // Activity
    Task UpsertActivityAsync(IEnumerable<DailyActivity> records);
    Task<List<DailyActivity>> GetActivityAsync(DateOnly from, DateOnly to);

    // Stress
    Task UpsertStressAsync(IEnumerable<DailyStress> records);
    Task<List<DailyStress>> GetStressAsync(DateOnly from, DateOnly to);

    // Resilience
    Task UpsertResilienceAsync(IEnumerable<DailyResilience> records);
    Task<List<DailyResilience>> GetResilienceAsync(DateOnly from, DateOnly to);

    // Cardiovascular Age
    Task UpsertCardiovascularAgeAsync(IEnumerable<DailyCardiovascularAge> records);
    Task<List<DailyCardiovascularAge>> GetCardiovascularAgeAsync(DateOnly from, DateOnly to);

    // SpO2
    Task UpsertSpo2Async(IEnumerable<DailySpo2> records);
    Task<List<DailySpo2>> GetSpo2Async(DateOnly from, DateOnly to);

    // Heart Rate
    Task UpsertHeartRateAsync(IEnumerable<HeartRateSample> samples);
    Task<List<HeartRateSample>> GetHeartRateAsync(DateTime from, DateTime to);

    // VO2 Max
    Task UpsertVo2MaxAsync(IEnumerable<Vo2MaxRecord> records);
    Task<List<Vo2MaxRecord>> GetVo2MaxAsync(DateOnly from, DateOnly to);

    // Workouts
    Task UpsertWorkoutsAsync(IEnumerable<Workout> workouts);
    Task<List<Workout>> GetWorkoutsAsync(DateOnly from, DateOnly to);

    // Nutrition
    Task UpsertNutritionAsync(IEnumerable<DailyNutrition> entries);
    Task<List<DailyNutrition>> GetNutritionAsync(DateOnly from, DateOnly to);

    // Meals
    Task<MealEntry> AddMealAsync(MealEntry meal);
    Task<MealEntry?> GetMealAsync(Guid id);
    Task<List<MealEntry>> GetMealsAsync(DateOnly day);
    Task<MealEntry?> UpdateMealAsync(MealEntry meal);
    Task<bool> DeleteMealAsync(Guid id);

    // Sync tracking
    Task<DateOnly?> GetLatestDayAsync();
}
