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
public static class LocalTime
{
    // IANA on Linux and macOS, Windows ids on Windows. .NET resolves both on modern
    // runtimes, but not universally, so both spellings are tried before giving up.
    private static readonly string[] Candidates = ["America/New_York", "Eastern Standard Time"];

    private static readonly Lazy<TimeZoneInfo> Zone = new(() =>
    {
        var configured = Environment.GetEnvironmentVariable("VITARA_TIMEZONE");
        var ids = string.IsNullOrWhiteSpace(configured) ? Candidates : [configured, .. Candidates];

        foreach (var id in ids)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        // Falling back to the machine's own zone is wrong but recoverable; throwing
        // here would take the whole module down over a timezone database. The container
        // sets TZ anyway, so this is very nearly always correct in practice.
        return TimeZoneInfo.Local;
    });

    public static TimeZoneInfo TimeZone => Zone.Value;

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);

    public static DateOnly Today => DateOnly.FromDateTime(Now);

    // The day that has just finished, which is what a job running after midnight is
    // actually reporting on.
    public static DateOnly Yesterday => Today.AddDays(-1);

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public static DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    // Midnight local, expressed in UTC — for querying stores that hold UTC instants.
    public static DateTime StartOfDayUtc(DateOnly day)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), TimeZone);
    }

    public static DateTime EndOfDayUtc(DateOnly day) => StartOfDayUtc(day.AddDays(1));

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
