using San.Application;

namespace San.Tests;

// San created a reminder for 10:30am and told the user 2:30pm. Four hours out, which
// is this timezone's offset exactly: the instant was right, the reading of it was not.
//
// Two faults behind one symptom. SQLite returns DateTime with Kind.Unspecified, so the
// API serialised the instant with no Z -- and a UTC timestamp with nothing marking it
// as UTC gets read as wall-clock by the model AND by JavaScript. Then, even with a Z,
// asking a model to do offset arithmetic on every row is a needless place to be wrong.
public class LocalTimeTextTests
{
    // Eastern, where the reported bug happened. Falls back to a fixed offset on a
    // machine without the tz database so the test measures the conversion, not the host.
    private static readonly TimeZoneInfo Eastern = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("fixed-minus-4", TimeSpan.FromHours(-4), "UTC-4", "UTC-4");
    }

    [Fact]
    public void TheReportedBug()
    {
        // Stored 14:30 UTC. The user asked for 10:30am and must be told 10:30 AM.
        var stored = new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Unspecified);

        Assert.Contains("10:30 AM", LocalTimeText.Local(stored, Eastern));
    }

    [Fact]
    public void TheDayTravelsWithTheTime()
    {
        // "10:30 AM" answers what time but not whether that is today, and "remind me
        // tomorrow" is the most common thing this system is asked to do.
        var text = LocalTimeText.Local(new DateTime(2026, 9, 10, 14, 30, 0), Eastern);

        Assert.Contains("10 Sep 2026", text);
        Assert.Contains("Thu", text);
    }

    [Fact]
    public void KindIsStampedSoTheInstantSerialisesUnambiguously()
    {
        // The root cause. Without this the JSON carries no Z, and JavaScript parses
        // the string as local time -- the web UI had the identical bug from it.
        var stored = new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(DateTimeKind.Utc, LocalTimeText.AsUtc(stored).Kind);
        Assert.Contains("Z", LocalTimeText.AsUtc(stored).ToString("o"));
    }

    [Fact]
    public void StampingDoesNotShiftTheInstant()
    {
        // SpecifyKind must relabel, never convert. Converting here would move every
        // stored reminder by the offset and break the notifications that work today.
        var stored = new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(14, LocalTimeText.AsUtc(stored).Hour);
    }

    [Fact]
    public void AnAlreadyUtcValueIsLeftAlone()
        => Assert.Equal(
            new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc),
            LocalTimeText.AsUtc(new DateTime(2026, 9, 10, 14, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void NullStaysNull()
    {
        // TriggerAt and NotifiedAt are both optional, and a formatted "01 Jan 0001"
        // would read as a real date to anything downstream.
        Assert.Null(LocalTimeText.AsUtc((DateTime?)null));
        Assert.Null(LocalTimeText.Local((DateTime?)null, Eastern));
    }
}
