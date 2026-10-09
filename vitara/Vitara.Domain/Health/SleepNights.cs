using Vitara.Domain.Entities;

namespace Vitara.Domain.Health;

// Which sleep session is "the night", and which sessions make up a day's total.
//
// The sources say what each session is, and that is believed before anything is guessed.
//
// OURA labels every period it sends (SleepSession.Type): long_sleep is the night; sleep and
// late_nap are naps or extra periods; rest is a rest period, not sleep; deleted is a period the
// person deleted in the app. Oura's own app shows the long_sleep as the night.
//
// APPLE HEALTH has no such label: its sleep is a set of stage spans (core, deep, REM, awake) with
// no night-or-nap flag. Vitara assembles one night per day from them and marks it AppleNight.
//
// Rows stored before Type existed have none. For those, and only those, the longest session is
// the night -- the old rule, kept as the last resort.
//
// TWO SOURCES ARE NEVER ADDED. Oura writes its night to Apple Health, so the same night can arrive
// twice; summing both doubled it. A day with any Oura sleep is Oura's day; Apple's night counts
// only on a day Oura has nothing.
public static class SleepNights
{
    public const string OuraNight = "long_sleep";
    public const string Deleted = "deleted";
    public const string Rest = "rest";

    // Vitara's own label for a night assembled from Apple Health's stages.
    public const string AppleNight = "apple_night";

    // Apple rows written before Type existed carry the source in their id instead.
    public static bool IsApple(SleepSession s) =>
        s.Type == AppleNight
        || s.Id.StartsWith("healthkit-", StringComparison.Ordinal)
        || s.Id.StartsWith("applehealth-", StringComparison.Ordinal);

    private static bool IsSleep(SleepSession s) => s.Type is not (Deleted or Rest);

    public static SleepSession Main(IEnumerable<SleepSession> sessionsOnOneDay)
    {
        var all = sessionsOnOneDay.ToList();
        var real = all.Where(IsSleep).ToList();

        static SleepSession Longest(IEnumerable<SleepSession> xs) =>
            xs.OrderByDescending(s => s.TotalSleepMinutes).ThenBy(s => s.Id, StringComparer.Ordinal).First();

        // In order: Oura's own night; an Oura night stored before the label existed (it has
        // the stages and HRV an Apple night lacks); Apple's night; then any other real sleep.
        var oura = real.Where(s => s.Type == OuraNight).ToList();
        if (oura.Count > 0) return Longest(oura);

        var unlabelledOura = real.Where(s => s.Type is null && !IsApple(s)).ToList();
        if (unlabelledOura.Count > 0) return Longest(unlabelledOura);

        var apple = real.Where(IsApple).ToList();
        if (apple.Count > 0) return Longest(apple);

        if (real.Count > 0) return Longest(real);
        return Longest(all);                                     // nothing but deleted/rest: still answer
    }

    // One night per day, oldest first.
    public static List<SleepSession> MainPerDay(IEnumerable<SleepSession> sessions) =>
        sessions.GroupBy(s => s.Day)
            .Select(g => Main(g))
            .OrderBy(s => s.Day)
            .ToList();

    // True while any of these Oura sessions was stored without Oura's label. The sync uses it to
    // fetch the recent window again once, so the labels get written in. Apple rows never have
    // an Oura label to fetch and do not count.
    public static bool NeedsLabels(IEnumerable<SleepSession> sessions) =>
        sessions.Any(s => s.Type is null && !IsApple(s));

    // The sessions a day's TOTAL is summed over: Oura's real sleep if Oura has any that day,
    // otherwise Apple's night. A genuine nap counts toward the total; a deleted period, a rest
    // period and Apple's copy of an Oura night do not.
    public static List<SleepSession> Countable(IEnumerable<SleepSession> sessionsOnOneDay)
    {
        var real = sessionsOnOneDay.Where(IsSleep).ToList();
        var notApple = real.Where(s => !IsApple(s)).ToList();
        return notApple.Count > 0 ? notApple : real.Where(IsApple).Take(1).ToList();
    }
}
