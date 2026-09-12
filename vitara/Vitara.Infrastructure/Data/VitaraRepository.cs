using Microsoft.EntityFrameworkCore;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.Infrastructure.Data;

public class VitaraRepository(VitaraDbContext db) : IVitaraRepository
{
    // ── Token ──
    public async Task<OuraToken?> GetTokenAsync() =>
        await db.Tokens.OrderByDescending(t => t.LinkedAt).FirstOrDefaultAsync();

    public async Task SaveTokenAsync(OuraToken token)
    {
        var existing = await db.Tokens.FirstOrDefaultAsync();
        if (existing is null) db.Tokens.Add(token);
        else { existing.AccessToken = token.AccessToken; existing.RefreshToken = token.RefreshToken; existing.ExpiresAt = token.ExpiresAt; }
        await db.SaveChangesAsync();
    }

    public async Task DeleteTokenAsync()
    {
        db.Tokens.RemoveRange(db.Tokens);
        await db.SaveChangesAsync();
    }

    // ── Profile ──
    public async Task<UserProfile?> GetProfileAsync() =>
        await db.Profiles.FirstOrDefaultAsync();

    public async Task SaveProfileAsync(UserProfile profile)
    {
        var existing = await db.Profiles.FindAsync(profile.Id);
        if (existing is null) db.Profiles.Add(profile);
        else db.Entry(existing).CurrentValues.SetValues(profile);
        await db.SaveChangesAsync();
    }

    // ── Generic upsert + query helpers ──
    private async Task UpsertByIdAsync<T>(DbSet<T> set, IEnumerable<T> items, Func<T, object> keySelector) where T : class
    {
        foreach (var item in items)
        {
            var key = keySelector(item);
            var ex = await set.FindAsync(key);
            if (ex is null) set.Add(item);
            else db.Entry(ex).CurrentValues.SetValues(item);
        }
        await db.SaveChangesAsync();
    }

    // ── Sleep ──
    public Task UpsertSleepAsync(IEnumerable<SleepSession> sessions) => UpsertByIdAsync(db.Sleep, sessions, s => s.Id);
    public Task<List<SleepSession>> GetSleepAsync(DateOnly from, DateOnly to) =>
        db.Sleep.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.Day).ToListAsync();

    // ── Readiness ──
    public Task UpsertReadinessAsync(IEnumerable<DailyReadiness> records) => UpsertByIdAsync(db.Readiness, records, r => r.Id);
    public Task<List<DailyReadiness>> GetReadinessAsync(DateOnly from, DateOnly to) =>
        db.Readiness.Where(r => r.Day >= from && r.Day <= to).OrderBy(r => r.Day).ToListAsync();

    // ── Activity ──
    public Task UpsertActivityAsync(IEnumerable<DailyActivity> records) => UpsertByIdAsync(db.Activity, records, a => a.Id);
    public Task<List<DailyActivity>> GetActivityAsync(DateOnly from, DateOnly to) =>
        db.Activity.Where(a => a.Day >= from && a.Day <= to).OrderBy(a => a.Day).ToListAsync();

    // ── Stress ──
    public Task UpsertStressAsync(IEnumerable<DailyStress> records) => UpsertByIdAsync(db.Stress, records, s => s.Id);
    public Task<List<DailyStress>> GetStressAsync(DateOnly from, DateOnly to) =>
        db.Stress.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.Day).ToListAsync();

    // ── Resilience ──
    public Task UpsertResilienceAsync(IEnumerable<DailyResilience> records) => UpsertByIdAsync(db.Resilience, records, r => r.Id);
    public Task<List<DailyResilience>> GetResilienceAsync(DateOnly from, DateOnly to) =>
        db.Resilience.Where(r => r.Day >= from && r.Day <= to).OrderBy(r => r.Day).ToListAsync();

    // ── Cardiovascular Age ──
    public Task UpsertCardiovascularAgeAsync(IEnumerable<DailyCardiovascularAge> records) => UpsertByIdAsync(db.CardiovascularAge, records, c => c.Id);
    public Task<List<DailyCardiovascularAge>> GetCardiovascularAgeAsync(DateOnly from, DateOnly to) =>
        db.CardiovascularAge.Where(c => c.Day >= from && c.Day <= to).OrderBy(c => c.Day).ToListAsync();

    // ── SpO2 ──
    public Task UpsertSpo2Async(IEnumerable<DailySpo2> records) => UpsertByIdAsync(db.Spo2, records, s => s.Id);
    public Task<List<DailySpo2>> GetSpo2Async(DateOnly from, DateOnly to) =>
        db.Spo2.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.Day).ToListAsync();

    public async Task<int> ReplaceMealsForDayAsync(DateOnly day, string source, IEnumerable<MealEntry> meals)
    {
        var incoming = meals.ToList();

        // Only this source's rows for this day. Manual entries and any other source
        // survive untouched, which is the whole reason MealEntry carries a source.
        var existing = await db.Meals.Where(m => m.Day == day && m.Source == source).ToListAsync();
        if (existing.Count > 0) db.Meals.RemoveRange(existing);

        foreach (var m in incoming)
        {
            m.Day = day;
            m.Source = source;
            db.Meals.Add(m);
        }

        await db.SaveChangesAsync();
        return incoming.Count;
    }

    // ── Health intelligence ──

    public async Task<int> UpsertObservationsAsync(IEnumerable<Observation> observations)
    {
        var incoming = observations.ToList();
        if (incoming.Count == 0) return 0;

        var from = incoming.Min(o => o.ObservedDateLocal);
        var to = incoming.Max(o => o.ObservedDateLocal);

        // One query for the whole batch. The alternative -- an existence check per row
        // -- is the pattern that made the heart-rate upsert slow, and a backfill writes
        // far more rows than a nightly sync does.
        var existing = (await db.Observations
                .Where(o => o.ObservedDateLocal >= from && o.ObservedDateLocal <= to)
                .Select(o => new { o.Metric, o.ObservedAtLocal, o.Source })
                .ToListAsync())
            .Select(o => (o.Metric, o.ObservedAtLocal, o.Source))
            .ToHashSet();

        var added = 0;
        foreach (var o in incoming)
            if (existing.Add((o.Metric, o.ObservedAtLocal, o.Source)))
            {
                db.Observations.Add(o);
                added++;
            }

        if (added > 0) await db.SaveChangesAsync();
        return added;
    }

    public async Task<List<Observation>> GetObservationsAsync(DateOnly from, DateOnly to, string? metric = null)
    {
        var q = db.Observations.Where(o => o.ObservedDateLocal >= from && o.ObservedDateLocal <= to);
        if (!string.IsNullOrWhiteSpace(metric)) q = q.Where(o => o.Metric == metric);
        return await q.OrderBy(o => o.ObservedDateLocal).ToListAsync();
    }

    public async Task<List<string>> GetObservedMetricsAsync() =>
        await db.Observations.Select(o => o.Metric).Distinct().ToListAsync();

    public async Task<DateOnly?> GetLatestObservationDayAsync() =>
        await db.Observations.OrderByDescending(o => o.ObservedDateLocal)
            .Select(o => (DateOnly?)o.ObservedDateLocal).FirstOrDefaultAsync();

    public async Task<DateOnly?> GetLatestBaselineDayAsync() =>
        await db.Baselines.OrderByDescending(b => b.ComputedOnLocal)
            .Select(b => (DateOnly?)b.ComputedOnLocal).FirstOrDefaultAsync();

    public async Task SaveBaselinesAsync(IEnumerable<Baseline> baselines)
    {
        var incoming = baselines.ToList();
        if (incoming.Count == 0) return;

        // Recomputing a day replaces it. A job that ran twice must not leave two
        // baselines for the same metric and day, or a later lookup picks arbitrarily.
        var days = incoming.Select(b => b.ComputedOnLocal).Distinct().ToList();
        var stale = await db.Baselines.Where(b => days.Contains(b.ComputedOnLocal)).ToListAsync();
        if (stale.Count > 0) db.Baselines.RemoveRange(stale);

        db.Baselines.AddRange(incoming);
        await db.SaveChangesAsync();
    }

    public Task<List<Baseline>> GetBaselinesAsync(DateOnly computedOn) =>
        db.Baselines.Where(b => b.ComputedOnLocal == computedOn).ToListAsync();

    public async Task SaveDerivedMetricsAsync(IEnumerable<DerivedMetric> metrics)
    {
        var incoming = metrics.ToList();
        if (incoming.Count == 0) return;

        var days = incoming.Select(m => m.ObservedDateLocal).Distinct().ToList();
        var names = incoming.Select(m => m.Metric).Distinct().ToList();
        var stale = await db.DerivedMetrics
            .Where(m => days.Contains(m.ObservedDateLocal) && names.Contains(m.Metric)).ToListAsync();
        if (stale.Count > 0) db.DerivedMetrics.RemoveRange(stale);

        db.DerivedMetrics.AddRange(incoming);
        await db.SaveChangesAsync();
    }

    public Task<List<DerivedMetric>> GetDerivedMetricsAsync(DateOnly from, DateOnly to) =>
        db.DerivedMetrics
            .Where(m => m.ObservedDateLocal >= from && m.ObservedDateLocal <= to)
            .OrderBy(m => m.ObservedDateLocal)
            .ToListAsync();

    public async Task<FindingSync> SyncFindingsAsync(IEnumerable<Finding> detected, DateOnly asOf)
    {
        var incoming = detected.ToList();

        // Every finding still open, whether or not this pass detected anything. An
        // empty detection set is meaningful: it means everything open has resolved.
        var open = await db.Findings.Where(f => f.ResolvedLocal == null).ToListAsync();
        var byKey = open.ToDictionary(f => f.Key);

        int opened = 0, continued = 0;

        foreach (var f in incoming)
        {
            if (byKey.TryGetValue(f.Key, out var existing))
            {
                // FirstDetectedLocal is deliberately left alone. It is what lets the
                // finding be reported as "for the fourth morning" rather than as
                // something new every single day, which is the difference between a
                // system that is tracking a condition and one that is just repeating
                // itself.
                existing.LastDetectedLocal = asOf;
                existing.Severity = f.Severity;
                existing.Confidence = f.Confidence;
                existing.Summary = f.Summary;
                existing.EvidenceJson = f.EvidenceJson;
                continued++;
            }
            else
            {
                db.Findings.Add(f);
                opened++;
            }
        }

        // Open, and not detected this pass: the condition has ended. Recorded rather
        // than removed -- "your resting heart rate is back to normal" is worth as much
        // as the original finding was, and a deleted row can never say it.
        var stillDetected = incoming.Select(f => f.Key).ToHashSet();
        var resolved = open.Where(f => !stillDetected.Contains(f.Key)).ToList();
        foreach (var f in resolved) f.ResolvedLocal = asOf;

        await db.SaveChangesAsync();
        return new FindingSync(opened, continued, resolved.Count);
    }

    public Task<List<Finding>> GetFindingsAsync(bool activeOnly = true, int limit = 100) =>
        db.Findings
            .Where(f => !activeOnly || f.ResolvedLocal == null)
            .OrderByDescending(f => f.LastDetectedLocal)
            .ThenByDescending(f => f.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync();

    public async Task<int> UpsertMeasurementsAsync(IEnumerable<Measurement> measurements)
    {
        var incoming = measurements.ToList();
        if (incoming.Count == 0) return 0;

        var from = incoming.Min(m => m.Day);
        var to = incoming.Max(m => m.Day);

        // Compared on the same triple the unique index uses. Reading the existing window
        // once beats letting the index throw on the first duplicate, which would make a
        // partial re-import unusable rather than merely redundant.
        var existing = (await db.Measurements
                .Where(m => m.Day >= from && m.Day <= to)
                .Select(m => new { m.Metric, m.ObservedAtLocal, m.Source })
                .ToListAsync())
            .Select(m => (m.Metric, m.ObservedAtLocal, m.Source))
            .ToHashSet();

        var fresh = incoming
            .Where(m => existing.Add((m.Metric, m.ObservedAtLocal, m.Source)))
            .ToList();

        if (fresh.Count == 0) return 0;

        db.Measurements.AddRange(fresh);
        await db.SaveChangesAsync();
        return fresh.Count;
    }

    public Task<List<Measurement>> GetMeasurementsAsync(DateOnly from, DateOnly to, string? metric = null) =>
        db.Measurements
            .Where(m => m.Day >= from && m.Day <= to && (metric == null || m.Metric == metric))
            .OrderByDescending(m => m.ObservedAtLocal)
            .ToListAsync();

    public async Task<bool> DeleteMeasurementAsync(Guid id)
    {
        var row = await db.Measurements.FirstOrDefaultAsync(m => m.Id == id);
        if (row is null) return false;

        db.Measurements.Remove(row);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task SaveCorrelationsAsync(IEnumerable<MetricCorrelation> correlations, DateOnly computedOn)
    {
        var stale = await db.Correlations.Where(c => c.ComputedOnLocal == computedOn).ToListAsync();
        if (stale.Count > 0) db.Correlations.RemoveRange(stale);

        db.Correlations.AddRange(correlations);
        await db.SaveChangesAsync();
    }

    public async Task<List<MetricCorrelation>> GetLatestCorrelationsAsync()
    {
        var day = await db.Correlations.OrderByDescending(c => c.ComputedOnLocal)
            .Select(c => (DateOnly?)c.ComputedOnLocal).FirstOrDefaultAsync();

        if (day is null) return [];

        return await db.Correlations
            .Where(c => c.ComputedOnLocal == day)
            .OrderByDescending(c => Math.Abs(c.Rho))
            .ToListAsync();
    }

    public Task<List<ExcludedPeriod>> GetExcludedPeriodsAsync() => db.ExcludedPeriods.ToListAsync();
    public Task<List<TravelPeriod>> GetTravelPeriodsAsync() => db.TravelPeriods.ToListAsync();
    public Task<List<Device>> GetDevicesAsync() => db.Devices.OrderBy(d => d.ActiveFromLocal).ToListAsync();

    public Task<SyncState?> GetSyncStateAsync(string source) =>
        db.SyncStates.FirstOrDefaultAsync(s => s.Source == source);

    public async Task SaveSyncStateAsync(SyncState state)
    {
        var existing = await db.SyncStates.FirstOrDefaultAsync(s => s.Source == state.Source);
        if (existing is null) db.SyncStates.Add(state);
        else
        {
            existing.LastSyncedAt = state.LastSyncedAt;
            existing.LastAttemptAt = state.LastAttemptAt;
            existing.LastError = state.LastError;
        }
        await db.SaveChangesAsync();
    }

    // ── Heart Rate ──
    // One query for the whole batch instead of one per sample. Oura samples heart rate
    // continuously, so a two-day pull is hundreds to low thousands of rows -- and the
    // previous version issued a separate SELECT for every one of them.
    public async Task UpsertHeartRateAsync(IEnumerable<HeartRateSample> samples)
    {
        var incoming = samples.ToList();
        if (incoming.Count == 0) return;

        var from = incoming.Min(s => s.Timestamp);
        var to = incoming.Max(s => s.Timestamp);

        var existing = (await db.HeartRate
                .Where(h => h.Timestamp >= from && h.Timestamp <= to)
                .Select(h => new { h.Timestamp, h.Bpm })
                .ToListAsync())
            .Select(h => (h.Timestamp, h.Bpm))
            .ToHashSet();

        // Oura returns overlapping windows between runs, and the same batch can repeat a
        // reading, so duplicates are filtered within the batch as well as against what
        // is already stored.
        foreach (var s in incoming)
            if (existing.Add((s.Timestamp, s.Bpm)))
                db.HeartRate.Add(s);

        await db.SaveChangesAsync();
    }

    // Nothing in this module has ever deleted a row. Heart rate is the one table where
    // that matters: it is the only high-volume one, and it lives on the same 16GB box
    // as the model.
    public async Task<int> PruneHeartRateAsync(DateTime before)
    {
        var stale = await db.HeartRate.Where(h => h.Timestamp < before).ToListAsync();
        if (stale.Count == 0) return 0;

        db.HeartRate.RemoveRange(stale);
        await db.SaveChangesAsync();
        return stale.Count;
    }
    public Task<List<HeartRateSample>> GetHeartRateAsync(DateTime from, DateTime to) =>
        db.HeartRate.Where(h => h.Timestamp >= from && h.Timestamp <= to).OrderBy(h => h.Timestamp).ToListAsync();

    // ── VO2 Max ──
    public Task UpsertVo2MaxAsync(IEnumerable<Vo2MaxRecord> records) => UpsertByIdAsync(db.Vo2Max, records, v => v.Id);
    public Task<List<Vo2MaxRecord>> GetVo2MaxAsync(DateOnly from, DateOnly to) =>
        db.Vo2Max.Where(v => v.Day >= from && v.Day <= to).OrderBy(v => v.Day).ToListAsync();

    // ── Workouts ──
    public Task UpsertWorkoutsAsync(IEnumerable<Workout> workouts) => UpsertByIdAsync(db.Workouts, workouts, w => w.Id);
    // Ordered by DAY first, then time within the day. Ordering by StartTime alone
    // silently hid every manually logged workout: StartTime is nullable and only Oura
    // supplies it, and SQLite sorts NULL last under DESC — so anything logged through
    // San or the UI ranked below every synced workout, and the callers that take the
    // most recent 3 or 10 then cut it off entirely. It was saved and invisible.
    public Task<List<Workout>> GetWorkoutsAsync(DateOnly from, DateOnly to) =>
        db.Workouts.Where(w => w.Day >= from && w.Day <= to)
            .OrderByDescending(w => w.Day).ThenByDescending(w => w.StartTime)
            .ToListAsync();

    // ── Nutrition ──
    public Task UpsertNutritionAsync(IEnumerable<DailyNutrition> entries) => UpsertByIdAsync(db.Nutrition, entries, n => n.Id);
    public Task<List<DailyNutrition>> GetNutritionAsync(DateOnly from, DateOnly to) =>
        db.Nutrition.Where(n => n.Day >= from && n.Day <= to).OrderBy(n => n.Day).ToListAsync();

    // ── Meals ──
    public async Task<MealEntry> AddMealAsync(MealEntry meal)
    {
        db.Meals.Add(meal);
        await db.SaveChangesAsync();
        return meal;
    }
    public Task<MealEntry?> GetMealAsync(Guid id) => db.Meals.FindAsync(id).AsTask();
    public Task<List<MealEntry>> GetMealsAsync(DateOnly day) =>
        db.Meals.Where(m => m.Day == day).OrderBy(m => m.MealType).ThenBy(m => m.LoggedAt).ToListAsync();
    public async Task<MealEntry?> UpdateMealAsync(MealEntry meal)
    {
        var existing = await db.Meals.FindAsync(meal.Id);
        if (existing is null) return null;
        existing.MealType = meal.MealType;
        existing.FoodName = meal.FoodName;
        existing.ServingQty = meal.ServingQty;
        existing.ServingUnit = meal.ServingUnit;
        existing.Calories = meal.Calories;
        existing.Protein = meal.Protein;
        existing.Carbs = meal.Carbs;
        existing.Fat = meal.Fat;
        existing.Fiber = meal.Fiber;
        await db.SaveChangesAsync();
        return existing;
    }
    public async Task<bool> DeleteMealAsync(Guid id)
    {
        var m = await db.Meals.FindAsync(id);
        if (m is null) return false;
        db.Meals.Remove(m);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Weigh-ins ──
    public Task UpsertWeighInAsync(WeighIn weighIn) => UpsertByIdAsync(db.WeighIns, new[] { weighIn }, w => w.Id);
    public Task<List<WeighIn>> GetWeighInsAsync(DateOnly from, DateOnly to) =>
        db.WeighIns.Where(w => w.Day >= from && w.Day <= to).OrderBy(w => w.Day).ToListAsync();

    // ── Sync tracking ──
    // The newest day each collection actually holds.
    //
    // The single watermark below takes the MIN across sleep, readiness and activity,
    // which correctly drags the window back when one of those three lags. It says
    // nothing about the other six, so a collection that failed for a fortnight was
    // never asked for those days again once the core three moved on.
    public async Task<Dictionary<string, DateOnly>> GetLatestDaysAsync()
    {
        var result = new Dictionary<string, DateOnly>();

        async Task Add(string name, IQueryable<DateOnly> days)
        {
            var latest = await days.OrderByDescending(d => d).Select(d => (DateOnly?)d).FirstOrDefaultAsync();
            if (latest.HasValue) result[name] = latest.Value;
        }

        // Keys match the names the sync worker uses for each collection, so the worker
        // can look up its own watermark without a translation table between the two.
        await Add("sleep", db.Sleep.Select(x => x.Day));
        await Add("readiness", db.Readiness.Select(x => x.Day));
        await Add("activity", db.Activity.Select(x => x.Day));
        await Add("stress", db.Stress.Select(x => x.Day));
        await Add("resilience", db.Resilience.Select(x => x.Day));
        await Add("cv-age", db.CardiovascularAge.Select(x => x.Day));
        await Add("spo2", db.Spo2.Select(x => x.Day));
        await Add("vo2max", db.Vo2Max.Select(x => x.Day));
        await Add("workouts", db.Workouts.Select(x => x.Day));

        return result;
    }

    public async Task<DateOnly?> GetLatestDayAsync()
    {
        var sleepMax = await db.Sleep.OrderByDescending(s => s.Day).Select(s => (DateOnly?)s.Day).FirstOrDefaultAsync();
        var readMax  = await db.Readiness.OrderByDescending(r => r.Day).Select(r => (DateOnly?)r.Day).FirstOrDefaultAsync();
        var actMax   = await db.Activity.OrderByDescending(a => a.Day).Select(a => (DateOnly?)a.Day).FirstOrDefaultAsync();

        var candidates = new[] { sleepMax, readMax, actMax }.Where(d => d.HasValue).Select(d => d!.Value).ToList();
        return candidates.Count > 0 ? candidates.Min() : null;
    }
}
