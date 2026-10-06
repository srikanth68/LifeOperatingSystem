namespace Maaya.Time;

// What day it is, for the person who lives here.
//
// ONE ANSWER, USED EVERYWHERE. Before this each module worked it out for itself and they
// did not agree: Vitara had its own helper, San resolved the zone from a NorthStar fact,
// Karma and the workers asked the process clock, and about forty controllers turned the
// UTC clock straight into a calendar day. The last group is the one that hurt.
//
//   DateOnly.FromDateTime(DateTime.UtcNow)
//
// reads as "today" and is "today in London". New York is four or five hours behind UTC,
// so from 8pm local onward it returns TOMORROW. A weigh-in at 9pm is filed under a day
// that has not started, the dashboard's "today" empties itself every evening, and a
// transaction window ends a day in the future. Nothing errors and every number is
// plausible, which is how it survived: it is wrong for four hours of every day and
// invisible in the other twenty.
//
// The same mistake runs the other way on the display side -- a UTC instant formatted with
// the viewer's clock instead of the configured one -- and the web app has a matching
// helper for that half (services/timezone.ts). Both read the same setting.
//
// WHICH ZONE, in order:
//
//   1. Configure(), when something at runtime has a better answer than the environment
//   2. MAAYA_TIMEZONE   -- the explicit, deliberate setting
//   3. TZ               -- the container's zone, which the Dockerfile sets from the same
//                          place as everything else, so the two cannot disagree
//   4. America/New_York -- the stated default for this system
//
// The machine's own zone is deliberately NOT in that list. A developer's laptop is not in
// the zone the system is configured for, and letting it win means the same code computes
// different days on the dev machine and on Everest -- the kind of difference that only
// shows up in production, at 8pm.
//
// DST is the reason this is an IANA id and not a fixed offset. "EST" literally means
// UTC-5 all year; the zone people in New York actually live in is UTC-4 from March to
// November, and a fixed offset would put every summer evening an hour off.
public static class MaayaClock
{
    public const string DefaultZoneId = "America/New_York";

    // IANA on Linux and macOS, Windows ids on Windows. Modern .NET resolves both on both,
    // but not universally, so each configured id is tried alongside its counterpart.
    private static readonly string[] Fallbacks = [DefaultZoneId, "Eastern Standard Time"];

    private static TimeZoneInfo? _override;
    private static readonly Lazy<TimeZoneInfo> Resolved = new(Resolve);

    public static TimeZoneInfo Zone => _override ?? Resolved.Value;

    public static string ZoneId => Zone.Id;

    // For a setting that can change while the process runs. Null clears it.
    public static void Configure(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) { _override = null; return; }

        // An unknown id leaves the previous zone in place rather than throwing. A bad
        // value typed into a settings screen must not take a module down.
        if (TryFind(zoneId.Trim()) is { } zone) _override = zone;
    }

    // Test seam and nothing else.
    internal static void Reset() => _override = null;

    // ── Now ─────────────────────────────────────────────────────────────────────

    // The wall clock in the configured zone. Kind is Unspecified on purpose: it is a
    // local reading, not an instant, and tagging it Utc or Local would invite exactly
    // the conversion mistakes this class exists to prevent.
    public static DateTime Now => FromUtc(DateTime.UtcNow);

    public static DateOnly Today => DateOnly.FromDateTime(Now);

    public static DateOnly Yesterday => Today.AddDays(-1);

    // ── Instants and days ───────────────────────────────────────────────────────

    // A stored instant, as the wall clock it was in the configured zone.
    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), Zone);

    // The calendar day an instant fell on. This is the correct form of
    // DateOnly.FromDateTime(<a UTC value>).
    public static DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(FromUtc(utc));

    // A wall-clock reading in the configured zone, back to an instant.
    //
    // Local times that do not exist (the spring-forward hour) or exist twice (the
    // fall-back hour) are resolved rather than thrown on. A reminder set for 2:30am on
    // the night the clocks change should still fire at some point that night, not fail.
    public static DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (Zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    // Midnight at the start of a local day, as an instant. For querying stores that hold
    // UTC: "everything that happened today" is [StartOfDayUtc(today), EndOfDayUtc(today)),
    // and bounding it with UTC midnight instead misattributes four or five hours.
    public static DateTime StartOfDayUtc(DateOnly day) => ToUtc(day.ToDateTime(TimeOnly.MinValue));

    // Start of the NEXT day, not 23:59:59.999. Exclusive upper bound, so no instant falls
    // between two days and none is counted in both -- and so a 25-hour day (fall back)
    // and a 23-hour day (spring forward) are both correct without special-casing.
    public static DateTime EndOfDayUtc(DateOnly day) => StartOfDayUtc(day.AddDays(1));

    // ── Parsing ─────────────────────────────────────────────────────────────────

    // A day arriving over the wire: "2026-10-03", with nothing about a zone, because a
    // calendar date has none. Null for anything else, so a caller can fall back to
    // Today without a try/catch.
    public static DateOnly? ParseDay(string? raw) =>
        DateOnly.TryParseExact(raw?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var day) ? day : null;

    // ── Resolution ──────────────────────────────────────────────────────────────

    private static TimeZoneInfo Resolve()
    {
        var wanted = new[]
        {
            Environment.GetEnvironmentVariable("MAAYA_TIMEZONE"),
            Environment.GetEnvironmentVariable("TZ"),
        };

        foreach (var id in wanted.Where(id => !string.IsNullOrWhiteSpace(id)))
            if (TryFind(id!.Trim()) is { } zone) return zone;

        foreach (var id in Fallbacks)
            if (TryFind(id) is { } zone) return zone;

        // Reaching here means the host has no timezone database at all. Throwing would
        // take the module down over tzdata; the machine's zone is wrong but recoverable,
        // and the container image installs tzdata explicitly so this is very nearly
        // unreachable in practice.
        return TimeZoneInfo.Local;
    }

    private static TimeZoneInfo? TryFind(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
