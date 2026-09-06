using Vitara.Domain.Entities;

namespace Vitara.Application.Interfaces;

public interface IVitaraRepository
{
    // Token
    Task<OuraToken?> GetTokenAsync();
    Task SaveTokenAsync(OuraToken token);
    Task DeleteTokenAsync();

    // The newest day held by EACH collection, keyed by the names the sync worker uses.
    //
    // Replaces a single watermark taken across sleep, readiness and activity only. That
    // one worked for those three and silently stranded the other six: if spo2 failed
    // for a fortnight while sleep kept succeeding, the window started a day before the
    // latest SLEEP record and those spo2 days were never fetched again.
    Task<Dictionary<string, DateOnly>> GetLatestDaysAsync();

    // Heart rate is the only high-volume table and nothing has ever removed a row from
    // it. Oura samples it continuously, so it grows without bound on a box that is also
    // hosting the model.
    Task<int> PruneHeartRateAsync(DateTime before);

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

    // Replaces one day's entries FROM ONE SOURCE, leaving every other source alone.
    //
    // MyFitnessPal entries get edited and deleted after the fact, so mirroring a day
    // means replacing it rather than merging -- and replacing it without the source
    // filter would delete food logged by hand through San.
    Task<int> ReplaceMealsForDayAsync(DateOnly day, string source, IEnumerable<MealEntry> meals);

    // Sync health for sources with no token of their own.
    Task<SyncState?> GetSyncStateAsync(string source);
    Task SaveSyncStateAsync(SyncState state);

    // Weigh-ins
    Task UpsertWeighInAsync(WeighIn weighIn);
    Task<List<WeighIn>> GetWeighInsAsync(DateOnly from, DateOnly to);

    // Sync tracking
    Task<DateOnly?> GetLatestDayAsync();
}
