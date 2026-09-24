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

    // ── Health intelligence ──

    // Ignores rows that already exist rather than failing the batch. The unique index
    // is what makes a backfill resumable, and a re-run that throws on the first
    // duplicate makes it unusable.
    Task<int> UpsertObservationsAsync(IEnumerable<Observation> observations);

    Task<List<Observation>> GetObservationsAsync(DateOnly from, DateOnly to, string? metric = null);
    Task<List<string>> GetObservedMetricsAsync();

    // The two watermarks the nightly job compares. Keeping the "already computed" mark
    // in the baselines themselves means there is no separate piece of state to fall out
    // of step with the data it describes.
    Task<DateOnly?> GetLatestObservationDayAsync();
    Task<DateOnly?> GetLatestBaselineDayAsync();

    Task SaveBaselinesAsync(IEnumerable<Baseline> baselines);
    Task<List<Baseline>> GetBaselinesAsync(DateOnly computedOn);
    Task SaveDerivedMetricsAsync(IEnumerable<DerivedMetric> metrics);
    Task<List<DerivedMetric>> GetDerivedMetricsAsync(DateOnly from, DateOnly to);

    // Reconciles a detection pass against what is already open.
    //
    // NOT an insert. A finding persists while the condition does, so the same elevated
    // resting heart rate on four consecutive mornings has to be one ongoing finding
    // that can be reported as "for the fourth day" -- not four identical rows, and not
    // four notifications. Matching is on Key, which the detectors derive from content
    // in code precisely so it is stable across runs.
    //
    // A finding that is open but no longer detected is RESOLVED, not deleted. The
    // condition ending is itself information, and a system that silently drops a row
    // can never tell the user that something went back to normal.
    Task<FindingSync> SyncFindingsAsync(IEnumerable<Finding> detected, DateOnly asOf);

    Task<List<Finding>> GetFindingsAsync(bool activeOnly = true, int limit = 100);

    // ── Manual / medium-tier readings ──
    //
    // Ignores rows that already exist rather than failing the batch, so re-importing an
    // overlapping Apple Health export is safe. Identity is the instant, not the day:
    // several blood pressure readings in a day are each real.
    Task<int> UpsertMeasurementsAsync(IEnumerable<Measurement> measurements);

    Task<List<Measurement>> GetMeasurementsAsync(DateOnly from, DateOnly to, string? metric = null);
    Task<bool> DeleteMeasurementAsync(Guid id);

    // Replaces the whole set for a day. A rerun must not leave two answers for the
    // same pair, and which one a later read picked would be arbitrary.
    Task SaveCorrelationsAsync(IEnumerable<MetricCorrelation> correlations, DateOnly computedOn);

    Task<List<MetricCorrelation>> GetLatestCorrelationsAsync();

    Task<List<ExcludedPeriod>> GetExcludedPeriodsAsync();
    Task<List<TravelPeriod>> GetTravelPeriodsAsync();
    Task<List<Device>> GetDevicesAsync();

    // What was deliberately started or stopped. Read when deciding whether a step
    // change in a metric has an explanation behind it.
    Task<List<Intervention>> GetInterventionsAsync();

    // Sync health for sources with no token of their own.
    Task<SyncState?> GetSyncStateAsync(string source);
    Task SaveSyncStateAsync(SyncState state);

    // Weigh-ins
    Task UpsertWeighInAsync(WeighIn weighIn);
    Task<List<WeighIn>> GetWeighInsAsync(DateOnly from, DateOnly to);

    // Sync tracking
    Task<DateOnly?> GetLatestDayAsync();
}

// What one detection pass did to the open set.
//
// Reported rather than inferred from row counts, because the three outcomes mean
// different things to the caller: only Opened is genuinely new, Continued is a
// condition that is still true and must not re-notify, and Resolved is the good news
// that has to travel too.
public record FindingSync(int Opened, int Continued, int Resolved);
