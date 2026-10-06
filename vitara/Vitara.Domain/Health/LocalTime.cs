using Maaya.Time;

namespace Vitara.Domain.Health;

// The user's day, not the server's.
//
// Every day boundary in Vitara is local. This matters more than it sounds: New York is
// four or five hours behind UTC, so anything after 7 or 8pm local is already tomorrow
// in UTC. Sleep beginning at 11pm on Tuesday gets filed under Wednesday, an evening
// blood-pressure reading lands on the wrong day, and every baseline built on top
// inherits the error.
//
// It is also why this has to be settled BEFORE the historical backfill. Loading two
// years of Oura data bucketed by UTC writes a corpus of subtly wrong day boundaries
// that nothing downstream can correct -- fixing it afterwards means re-pulling
// everything and recomputing every baseline.
//
// Oura itself reports a `day` field already in the user's local terms, so ingest
// should prefer that over deriving one. This exists for everything else: manual
// entries, "what is today", and the nightly job's notion of which day just ended.
//
// NOW A THIN LAYER OVER MaayaClock. This used to be its own implementation, which meant
// Vitara agreed with itself and disagreed with every other module -- the controllers
// that did not use it computed days from the UTC clock, and a weigh-in at 9pm was filed
// under tomorrow. The zone is resolved once, in one place, for the whole system.
// VITARA_TIMEZONE still wins when set, so an existing deployment keeps its behaviour.
public static class LocalTime
{
    static LocalTime()
    {
        if (Environment.GetEnvironmentVariable("VITARA_TIMEZONE") is { Length: > 0 } configured)
            MaayaClock.Configure(configured);
    }

    public static TimeZoneInfo TimeZone => MaayaClock.Zone;

    public static DateTime Now => MaayaClock.Now;

    public static DateOnly Today => MaayaClock.Today;

    // The day that has just finished, which is what a job running after midnight is
    // actually reporting on.
    public static DateOnly Yesterday => MaayaClock.Yesterday;

    public static DateTime ToLocal(DateTime utc) => MaayaClock.FromUtc(utc);

    public static DateOnly DayOf(DateTime utc) => MaayaClock.DayOf(utc);

    // Midnight local, expressed in UTC -- for querying stores that hold UTC instants.
    public static DateTime StartOfDayUtc(DateOnly day) => MaayaClock.StartOfDayUtc(day);

    public static DateTime EndOfDayUtc(DateOnly day) => MaayaClock.EndOfDayUtc(day);

    // Which part of the day a reading belongs to, for the metrics that baseline on it.
    // Derived rather than asked for, so logging a blood pressure stays one tap --
    // a reading that takes thirty seconds to record does not get recorded.
    public static string TimeOfDayBucket(DateTime local) => local.Hour switch
    {
        >= 4 and < 9 => "waking",
        >= 9 and < 12 => "morning",
        >= 12 and < 17 => "afternoon",
        >= 17 and < 22 => "evening",
        _ => "night",
    };
}
