using Vitara.Domain.Entities;

namespace Vitara.Domain.Health;

// When the ring and the phone both report a day's movement, which one counts
// (decided 2026-10-13, Vitara ADR 0025).
//
// The phone's health app wins. Apple Health and Health Connect are already where every
// ring and watch writes its steps and workouts, and they de-duplicate across those
// sources themselves -- so their daily total is the one figure that has already been
// reconciled. The ring keeps everything only it measures: its activity score and its
// minutes in each intensity band.
//
// Both rows are stored, each under its own source; nothing is overwritten. This decides
// what is READ.
public static class SourceResolution
{
    public const string PhoneHealth = "apple_health";

    public static bool IsPhone(DailyActivity a) => a.Source == PhoneHealth;

    // One row per day. Where both exist, the ring's row with the phone's steps and active
    // calories laid over it; a zero from the phone (no data that day) never wins.
    public static List<DailyActivity> Activity(IEnumerable<DailyActivity> rows) =>
        rows.GroupBy(a => a.Day)
            .Select(g =>
            {
                var phone = g.FirstOrDefault(IsPhone);
                var ring = g.FirstOrDefault(a => !IsPhone(a));
                if (phone is null) return ring!;
                if (ring is null) return phone;

                return new DailyActivity
                {
                    Id = ring.Id,
                    Day = ring.Day,
                    Source = phone.Steps > 0 || phone.ActiveCalories > 0 ? PhoneHealth : ring.Source,
                    Score = ring.Score,
                    Steps = phone.Steps > 0 ? phone.Steps : ring.Steps,
                    ActiveCalories = phone.ActiveCalories > 0 ? phone.ActiveCalories : ring.ActiveCalories,
                    TotalCalories = ring.TotalCalories,
                    EquivalentWalkingDistance = ring.EquivalentWalkingDistance,
                    HighActivityMinutes = ring.HighActivityMinutes,
                    MediumActivityMinutes = ring.MediumActivityMinutes,
                    LowActivityMinutes = ring.LowActivityMinutes,
                    SedentaryMinutes = ring.SedentaryMinutes,
                    RestMinutes = ring.RestMinutes,
                    AvgMet = ring.AvgMet,
                };
            })
            .OrderBy(a => a.Day)
            .ToList();

    public static bool IsPhone(Workout w) => w.Source == PhoneHealth || w.Id.StartsWith("applehealth-");

    // A ring workout that overlaps a phone workout is the same session seen twice: the
    // ring's copy is dropped. Workouts with no times (logged by hand) are always kept.
    public static List<Workout> Workouts(IEnumerable<Workout> workouts)
    {
        var all = workouts.ToList();
        var phone = all.Where(IsPhone).Where(w => w.StartTime is not null && w.EndTime is not null).ToList();

        return all.Where(w =>
        {
            if (IsPhone(w) || w.StartTime is not { } s || w.EndTime is not { } e) return true;
            return !phone.Any(p => Overlap(s, e, p.StartTime!.Value, p.EndTime!.Value));
        }).ToList();
    }

    // Half of the shorter session overlapping, or starts within ten minutes: either way
    // one session reported twice rather than two sessions.
    private static bool Overlap(DateTime s1, DateTime e1, DateTime s2, DateTime e2)
    {
        if (Math.Abs((s1 - s2).TotalMinutes) <= 10) return true;
        var overlap = (Min(e1, e2) - Max(s1, s2)).TotalMinutes;
        var shorter = Math.Min((e1 - s1).TotalMinutes, (e2 - s2).TotalMinutes);
        return shorter > 0 && overlap >= shorter / 2;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
