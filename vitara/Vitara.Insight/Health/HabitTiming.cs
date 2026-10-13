namespace Vitara.Insight.Health;

// Clock times turned into the forms worth analysing.
//
// A logged "last coffee at 16:00" is a poor correlation driver: it means something
// different to someone who sleeps at ten and someone who sleeps at two. Hours before that
// night's bedtime is the useful form. And bedtime regularity is a spread of clock times,
// which has to be measured around midnight or 23:30 and 00:30 average to noon.
//
// Pure. Days and minutes in, values out.
public static class HabitTiming
{
    // Before this, a logged time is the small hours of the same evening rather than the
    // morning: a last coffee "at 00:30" on a Friday was drunk after Friday's dinner.
    public const int SmallHoursEndMinutes = 5 * 60;

    // Readings needed in the fortnight before a regularity spread means anything.
    public const int RegularityWindowNights = 14;
    public const int RegularityMinNights = 7;

    // Hours between a logged clock time on day D and the bedtime of the night that
    // followed it. Oura files a night against the day you wake, so that night is D + 1.
    //
    // Refused, rather than computed, on a travel day: the log is in one clock and the
    // bedtime may be in another, and the difference between them is the time difference,
    // not the habit.
    public static List<(DateOnly Day, double Hours)> HoursBeforeBed(
        IReadOnlyList<(DateOnly Day, double MinutesAfterMidnight)> logs,
        IReadOnlyDictionary<DateOnly, double> bedtimeByWakeDay,
        IReadOnlyList<(DateOnly Start, DateOnly End)>? travel = null)
    {
        var result = new List<(DateOnly, double)>();

        foreach (var (day, logged) in logs)
        {
            if (!bedtimeByWakeDay.TryGetValue(day.AddDays(1), out var bed)) continue;
            if ((travel ?? []).Any(t => (day >= t.Start && day <= t.End) || (day.AddDays(1) >= t.Start && day.AddDays(1) <= t.End)))
                continue;

            // Both measured from midnight at the start of day D.
            var logMinutes = logged < SmallHoursEndMinutes ? logged + 1440 : logged;
            var bedMinutes = 1440 + bed;   // bed is minutes either side of the midnight ending D

            var hours = (bedMinutes - logMinutes) / 60.0;

            // Logged after the person was already asleep, or a day apart: a mistake in
            // one of the two times, and a number built on it would be a confident lie.
            if (hours < 0 || hours > 20) continue;

            result.Add((day, Math.Round(hours, 2)));
        }

        return result;
    }

    // The mean of clock times, taken on the circle. 23:30 and 00:30 give 00:00.
    public static double CircularMeanMinutes(IReadOnlyList<double> minutesAroundMidnight)
    {
        if (minutesAroundMidnight.Count == 0) return 0;
        var (s, c) = Sums(minutesAroundMidnight);
        var angle = Math.Atan2(s, c);
        var minutes = angle / (2 * Math.PI) * 1440;
        return Math.Round(minutes > 720 ? minutes - 1440 : minutes, 1);
    }

    // The circular standard deviation, in minutes: sqrt(-2 ln R) on the 24-hour circle.
    // For a spread of an hour or two it agrees with the ordinary standard deviation; the
    // difference is that it does not break across midnight.
    public static double CircularSdMinutes(IReadOnlyList<double> minutesAroundMidnight)
    {
        if (minutesAroundMidnight.Count < 2) return 0;
        var (s, c) = Sums(minutesAroundMidnight);
        var r = Math.Sqrt(s * s + c * c) / minutesAroundMidnight.Count;
        if (r >= 1) return 0;
        return Math.Round(Math.Sqrt(-2 * Math.Log(r)) * 1440 / (2 * Math.PI), 1);
    }

    // The bedtime spread over the fortnight ending on a day, or null with too few nights.
    public static double? Regularity(IReadOnlyDictionary<DateOnly, double> bedtimeByWakeDay, DateOnly asOf)
    {
        var nights = bedtimeByWakeDay
            .Where(kv => kv.Key > asOf.AddDays(-RegularityWindowNights) && kv.Key <= asOf)
            .Select(kv => kv.Value)
            .ToList();

        return nights.Count < RegularityMinNights ? null : CircularSdMinutes(nights);
    }

    private static (double Sin, double Cos) Sums(IReadOnlyList<double> minutes)
    {
        double s = 0, c = 0;
        foreach (var m in minutes)
        {
            var a = m / 1440.0 * 2 * Math.PI;
            s += Math.Sin(a);
            c += Math.Cos(a);
        }
        return (s, c);
    }
}
