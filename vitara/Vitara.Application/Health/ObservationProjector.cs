using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Application.Health;

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

    public static List<Observation> FromSleep(SleepSession s)
    {
        // Bedtime end is when the night is finished being measured, and is inside the
        // local day Oura assigned it. Using it keeps the observation's instant and its
        // day consistent with each other.
        var at = s.BedtimeEnd == default ? s.Day.ToDateTime(new TimeOnly(7, 0)) : s.BedtimeEnd;

        var observations = new List<Observation>
        {
            Make(MetricKeys.TotalSleepMinutes, s.TotalSleepMinutes, "min", s.Day, at, s.Id),
            Make(MetricKeys.DeepSleepMinutes, s.DeepMinutes, "min", s.Day, at, s.Id),
            Make(MetricKeys.RemSleepMinutes, s.RemMinutes, "min", s.Day, at, s.Id),
        };

        // Optional metrics are omitted when absent rather than written as zero. A
        // missing HRV reading is not an HRV of zero, and a baseline that averages in
        // the nights the ring was not worn is describing something other than the user.
        if (s.Score is { } score) observations.Add(Make(MetricKeys.SleepScore, score, "score", s.Day, at, s.Id));
        if (s.AvgHrv is { } hrv) observations.Add(Make(MetricKeys.HrvRmssd, hrv, "ms", s.Day, at, s.Id));
        if (s.LowestHr is { } hr) observations.Add(Make(MetricKeys.RestingHeartRate, hr, "bpm", s.Day, at, s.Id));
        if (s.AvgBreathingRate is { } br) observations.Add(Make(MetricKeys.BreathingRate, br, "brpm", s.Day, at, s.Id));
        if (s.AvgSpo2 is { } spo2) observations.Add(Make(MetricKeys.Spo2Average, spo2, "%", s.Day, at, s.Id));

        // The single most valuable metric here, and the reason the spec calls it out.
        // Combined with resting HR and HRV it is the earliest pre-symptomatic illness
        // signal available from this data, and it is easy to leave behind as an
        // incidental field on a sleep row.
        if (s.SkinTempDeviation is { } temp)
            observations.Add(Make(MetricKeys.SkinTempDeviation, temp, "C", s.Day, at, s.Id));

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
