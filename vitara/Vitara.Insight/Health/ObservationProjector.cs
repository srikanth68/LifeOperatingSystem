using System.Text.Json;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// Turns Vitara's typed rows into flat observations.
//
// This is the bridge that makes the analytics layer possible without rewriting
// anything. SleepSession, DailyReadiness and the rest stay exactly as they are and
// keep serving the dashboard and San; each one also becomes a handful of observation
// rows that the baseline machinery can treat uniformly.
//
// Pure and synchronous on purpose -- no database, no clock. Given the same typed rows
// it produces the same observations, which is what makes it testable and what makes
// re-running a backfill safe.
//
// Oura's day field is used as-is rather than derived from timestamps. Oura already
// reports the night in the user's local terms, and re-deriving it from a UTC instant
// is exactly how sleep starting at 11pm ends up filed under tomorrow.
public static class ObservationProjector
{
    private const string Oura = "oura";

    // ONE ROW PER NIGHT, not one per session.
    //
    // Oura's `sleep` endpoint returns every sleep period it detected, not just the
    // night -- naps, "rest" periods, and a good deal of noise. SleepSession has no type
    // field, so all of it was stored indistinguishably and each row became its own
    // observation. Real data from one database:
    //
    //     2026-09-04   13, 9, 5, 382 minutes
    //     2026-08-10   0, 504 minutes
    //
    // Those 2-to-30 minute rows are not naps, they are the ring mis-detecting. Every
    // one of them was counted as a night: against a need of roughly seven hours, each
    // contributed a full night's deficit, and 2026-09-04 alone manufactured about
    // twenty-one hours of sleep debt. San reported fifty-one hours. It was also
    // wrecking the total_sleep_minutes baseline, which pooled 5-minute values with
    // 400-minute ones -- so the mean collapsed, the spread exploded, and every sleep
    // z-score computed against it was meaningless.
    //
    // Summing needs no threshold, which is why it beats trying to decide what counts as
    // a real nap. A handful of junk minutes added to a 400-minute night changes nothing;
    // a genuine 40-minute nap counts, which is correct for a total.
    //
    // The point-in-time metrics do NOT sum and are taken from the longest session. A
    // 5-minute nap's HRV averaged into the night's is a corrupted reading, and its skin
    // temperature is the number the illness detector reads.
    public static List<Observation> FromSleep(IReadOnlyList<SleepSession> sessionsOnOneDay)
    {
        if (sessionsOnOneDay.Count == 0) return [];

        var day = sessionsOnOneDay[0].Day;

        // The night, by duration. Ties do not matter -- any of them is as good a source
        // for a point-in-time reading as the other.
        var main = sessionsOnOneDay.OrderByDescending(s => s.TotalSleepMinutes).First();

        var at = main.BedtimeEnd == default ? day.ToDateTime(new TimeOnly(7, 0)) : main.BedtimeEnd;

        // Stable across a re-projection even if Oura adds or revises a session later:
        // the instant is what the unique index keys on, and a moving instant would leave
        // the old row behind next to the new one.
        var id = main.Id;

        var observations = new List<Observation>
        {
            Make(MetricKeys.TotalSleepMinutes, sessionsOnOneDay.Sum(s => s.TotalSleepMinutes), "min", day, at, id),
            Make(MetricKeys.DeepSleepMinutes, sessionsOnOneDay.Sum(s => s.DeepMinutes), "min", day, at, id),
            Make(MetricKeys.RemSleepMinutes, sessionsOnOneDay.Sum(s => s.RemMinutes), "min", day, at, id),
        };

        // Optional metrics are omitted when absent rather than written as zero. A
        // missing HRV reading is not an HRV of zero, and a baseline that averages in
        // the nights the ring was not worn is describing something other than the user.
        if (main.Score is { } score) observations.Add(Make(MetricKeys.SleepScore, score, "score", day, at, id));
        if (main.AvgHrv is { } hrv) observations.Add(Make(MetricKeys.HrvRmssd, hrv, "ms", day, at, id));
        if (main.LowestHr is { } hr) observations.Add(Make(MetricKeys.RestingHeartRate, hr, "bpm", day, at, id));
        if (main.AvgBreathingRate is { } br) observations.Add(Make(MetricKeys.BreathingRate, br, "brpm", day, at, id));
        if (main.AvgSpo2 is { } spo2) observations.Add(Make(MetricKeys.Spo2Average, spo2, "%", day, at, id));

        // The single most valuable metric here, and the reason the spec calls it out.
        // Combined with resting HR and HRV it is the earliest pre-symptomatic illness
        // signal available from this data, and it is easy to leave behind as an
        // incidental field on a sleep row.
        if (main.SkinTempDeviation is { } temp)
            observations.Add(Make(MetricKeys.SkinTempDeviation, temp, "C", day, at, id));

        return observations;
    }

    public static List<Observation> FromReadiness(DailyReadiness r)
    {
        var at = r.Day.ToDateTime(new TimeOnly(7, 0));
        var observations = new List<Observation>();

        if (r.Score is { } score) observations.Add(Make(MetricKeys.ReadinessScore, score, "score", r.Day, at, r.Id));

        // Oura reports resting heart rate on both the sleep and readiness endpoints.
        // Only one may be projected: the unique index is (metric, instant, source), and
        // two sources of the same truth would either collide or silently double-count
        // in a baseline. Sleep wins because it carries the measured value rather than
        // a contributor score.
        return observations;
    }

    public static List<Observation> FromActivity(DailyActivity a)
    {
        var at = a.Day.ToDateTime(new TimeOnly(21, 0));   // a day's activity is complete by evening
        var observations = new List<Observation>
        {
            Make(MetricKeys.Steps, a.Steps, "steps", a.Day, at, a.Id),
            Make(MetricKeys.ActiveCalories, a.ActiveCalories, "kcal", a.Day, at, a.Id),
        };

        if (a.Score is { } score) observations.Add(Make(MetricKeys.ActivityScore, score, "score", a.Day, at, a.Id));
        return observations;
    }

    public static List<Observation> FromStress(DailyStress s)
    {
        if (s.StressHighSeconds is not { } seconds) return [];
        return [Make(MetricKeys.StressHighSeconds, seconds, "s", s.Day, s.Day.ToDateTime(new TimeOnly(21, 0)), s.Id)];
    }

    public static List<Observation> FromSpo2(DailySpo2 s)
    {
        if (s.Spo2Average is not { } value) return [];
        return [Make(MetricKeys.Spo2Average, value, "%", s.Day, s.Day.ToDateTime(new TimeOnly(7, 30)), s.Id)];
    }

    public static List<Observation> FromVo2Max(Vo2MaxRecord v)
    {
        if (v.Vo2Max is not { } value) return [];
        return [Make(MetricKeys.Vo2Max, value, "ml/kg/min", v.Day, v.Day.ToDateTime(new TimeOnly(12, 0)), v.Id)];
    }

    public static List<Observation> FromCardiovascularAge(DailyCardiovascularAge c)
    {
        if (c.VascularAge is not { } value) return [];
        return [Make(MetricKeys.CardiovascularAge, value, "years", c.Day, c.Day.ToDateTime(new TimeOnly(12, 0)), c.Id)];
    }

    public static List<Observation> FromWeighIn(WeighIn w)
    {
        var at = w.Day.ToDateTime(new TimeOnly(7, 0));
        var observation = Make(MetricKeys.WeightKg, w.WeightKg, "kg", w.Day, at, w.Id);

        observation.Source = "manual";
        observation.Tier = Tiers.Medium;

        // Weight baselines on time of day, and a weigh-in recorded without one cannot
        // join a bucket. Habitual morning weighing is assumed rather than demanded --
        // a reading that takes thirty seconds to log does not get logged.
        var context = new MeasurementContext(TimeOfDay: LocalTime.TimeOfDayBucket(at));
        observation.BaselineSignature = BaselineKeys.Signature(MetricKeys.WeightKg, context);
        observation.EligibleForBaseline = BaselineKeys.CanBaseline(MetricKeys.WeightKg, context);

        return [observation];
    }

    // A reading a person entered, or one imported from a phone.
    //
    // The one projection that carries a baseline signature. Everything Oura measures is
    // taken overnight under conditions the user does not vary; a blood pressure reading
    // is seated or standing, morning or evening, and those are different quantities
    // sharing a name. BaselineKeys decides which parts of the context actually split
    // the baseline -- the rest still travels, to explain an outlier later.
    public static Observation FromMeasurement(Measurement m)
    {
        MeasurementContext? context = null;
        if (!string.IsNullOrWhiteSpace(m.ContextJson))
        {
            // A malformed context must not lose the reading. The number is still good;
            // only the bucket it belongs in is unknown, and the unsplit bucket is the
            // right place for a reading whose conditions were not recorded.
            try { context = JsonSerializer.Deserialize<MeasurementContext>(m.ContextJson); }
            catch (JsonException) { }
        }

        return new Observation
        {
            Metric = m.Metric,
            Value = m.Value,
            Unit = m.Unit,
            ObservedAtLocal = m.ObservedAtLocal,
            ObservedDateLocal = m.Day,
            Tier = m.Tier,
            Source = m.Source,
            SourceRecordId = m.Id.ToString(),
            ContextJson = m.ContextJson,
            BaselineSignature = BaselineKeys.Signature(m.Metric, context),
            EligibleForBaseline = true,
        };
    }

    private static Observation Make(string metric, double value, string unit, DateOnly day, DateTime at, string? sourceId) => new()
    {
        Metric = metric,
        Value = value,
        Unit = unit,
        ObservedAtLocal = at,
        ObservedDateLocal = day,
        Tier = Tiers.Dense,
        Source = Oura,
        SourceRecordId = sourceId,
        // Anything a ring measured overnight has no context the user varied, so it does
        // not split a baseline and is always eligible for one.
        BaselineSignature = "",
        EligibleForBaseline = true,
    };
}
