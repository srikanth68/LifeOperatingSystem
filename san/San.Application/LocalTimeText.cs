namespace San.Application;

// Making a stored instant unambiguous on the way out.
//
// San created a reminder for 10:30am and then told the user it was set for 2:30pm --
// four hours out, which is exactly this timezone's offset. The write was correct and
// the reminder would have fired on time; only the read-back lied.
//
// Two separate faults, and both had to be fixed:
//
//   1. SQLite hands back DateTime with Kind.Unspecified, so the API serialised
//      "2026-09-10T14:30:00" with no Z. Nothing in that string says it is UTC. The
//      model read 14:30 and reported half past two, which is correct arithmetic on
//      wrong data -- and JavaScript parses the same string as LOCAL time, so the web
//      UI had the identical bug from the identical cause.
//
//   2. Even stamped with a Z, a model has to do offset arithmetic to say what time
//      something is, on every single row. That is a needless place to be wrong, so a
//      pre-formatted local string travels alongside the instant. Gemma reading
//      "10:30 AM" cannot get it wrong; "14:30Z" it can.
//
// The UTC field stays UTC. Converting it in place would have broken the write path,
// which correctly sends UTC, and left the two halves of a round trip disagreeing.
public static class LocalTimeText
{
    // Stamps the Kind that SQLite dropped, so the value serialises with a Z and every
    // consumer -- model, browser, another service -- reads the same instant.
    public static DateTime AsUtc(DateTime stored) =>
        stored.Kind == DateTimeKind.Utc ? stored : DateTime.SpecifyKind(stored, DateTimeKind.Utc);

    public static DateTime? AsUtc(DateTime? stored) => stored is { } d ? AsUtc(d) : null;

    // Wall-clock in the user's own timezone, spelled out.
    //
    // Day and date included rather than time alone: "10:30 AM" answers what time but
    // not whether that is today, and "remind me tomorrow" is the most common thing
    // this system is asked to do.
    public static string Local(DateTime storedUtc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(storedUtc), tz).ToString("ddd d MMM yyyy, h:mm tt");

    public static string? Local(DateTime? storedUtc, TimeZoneInfo tz) =>
        storedUtc is { } d ? Local(d, tz) : null;
}
