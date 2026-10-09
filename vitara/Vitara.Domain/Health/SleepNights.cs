using Vitara.Domain.Entities;

namespace Vitara.Domain.Health;

// Which sleep session is "the night".
//
// Oura's `sleep` endpoint returns every period it detected on a day: the night, naps, "rest"
// periods and short mis-detections, and SleepSession does not record which is which. Anything
// that wants ONE night per day -- last night on the home screen, a nightly average, a usual
// bedtime -- asks here, so the rule is written once.
//
// The night is the longest session of the day. That is what Oura's own app shows as the night,
// and it needs no threshold to decide what counts as a nap. Ties go to the earlier id so the
// answer does not depend on the order rows came back in.
//
// What this is NOT for: a daily TOTAL. Summing every session (ObservationProjector) is right for
// "how much did you sleep today", because a real nap counts toward that. The readings taken
// during sleep (HRV, lowest heart rate, skin temperature, the score) belong to the night only:
// a nap's HRV averaged into the night's is a corrupted reading.
public static class SleepNights
{
    public static SleepSession Main(IEnumerable<SleepSession> sessionsOnOneDay) =>
        sessionsOnOneDay
            .OrderByDescending(s => s.TotalSleepMinutes)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .First();

    // One night per day, oldest first.
    public static List<SleepSession> MainPerDay(IEnumerable<SleepSession> sessions) =>
        sessions.GroupBy(s => s.Day)
            .Select(g => Main(g))
            .OrderBy(s => s.Day)
            .ToList();
}
