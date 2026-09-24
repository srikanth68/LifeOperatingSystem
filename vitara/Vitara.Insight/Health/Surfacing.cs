using Vitara.Domain.Entities;

namespace Vitara.Insight.Health;

// What gets said out loud today, out of everything currently true.
//
// Detection and surfacing are different jobs, and conflating them is how a health
// system becomes something you mute. The detectors are tuned to be right; if they are
// right about six things at once, saying all six every morning is still the wrong
// behaviour. By the second week the reader has learned that the list is long and
// mostly unchanged, and stops reading it -- taking the one finding that mattered with
// it. That failure has already happened once on this project, on a notification
// channel, and the lesson was not "detect less".
//
// So nothing is suppressed and nothing is deleted: everything active stays active,
// stays in /findings, stays in the tab. This only decides which few lead.
//
// The ordering says: severity first; then how new it is, because a finding running for
// its fortieth day has been said thirty-nine times and the reader already knows. New
// beats old at the same severity, which is the opposite of ranking by duration and is
// the entire point -- duration is what makes a finding certain, not what makes it
// news.
public static class Surfacing
{
    // Three. Small enough to read on a phone before coffee, large enough that a real
    // morning -- illness coming on, training load high, a lab drifting -- is not cut
    // in half. Tunable, because the right number is a matter of temperament.
    public static int Cap =>
        int.TryParse(Environment.GetEnvironmentVariable("VITARA_SURFACED_MAX"), out var v) && v > 0 ? v : 3;

    public record Result(
        IReadOnlyList<Finding> Surfaced,
        IReadOnlyList<Finding> Standing,
        int Cap,
        // Written here rather than in each caller, so the tab, San and any future export
        // describe the held-back set the same way.
        string Note);

    public static Result Choose(IReadOnlyList<Finding> active) => Choose(active, Cap);

    public static Result Choose(IReadOnlyList<Finding> active, int cap)
    {
        if (active.Count == 0) return new Result([], [], cap, "");

        // Severe findings are never held back, even past the cap. A cap exists to stop
        // noise crowding out signal; applying it to "high" would make it do the exact
        // opposite on the one morning it matters. Four serious things at once is rare,
        // and if it happens the reader should be told four times.
        var severe = active.Where(f => f.Severity == "high").ToList();

        var rest = active
            .Where(f => f.Severity != "high")
            .OrderByDescending(f => Rank(f.Severity))
            .ThenBy(DaysRunning)                      // newest first at equal severity
            .ThenByDescending(f => f.LastDetectedLocal)
            .ThenBy(f => f.Key, StringComparer.Ordinal)  // deterministic, so runs agree
            .ToList();

        var room = Math.Max(0, cap - severe.Count);
        var surfaced = severe.Concat(rest.Take(room)).ToList();
        var standing = rest.Skip(room).ToList();

        return new Result(surfaced, standing, cap, Describe(standing));
    }

    // The sentence a reader needs to know nothing was swept away. Deliberately says
    // where the rest are, because "2 more" with no way to see them is worse than
    // listing all of them.
    private static string Describe(IReadOnlyList<Finding> standing) => standing.Count switch
    {
        0 => "",
        1 => "One more finding is standing and unchanged; it is listed in full under all findings.",
        _ => $"{standing.Count} more findings are standing and unchanged; they are listed in full under all findings.",
    };

    public static int DaysRunning(Finding f) =>
        f.LastDetectedLocal.DayNumber - f.FirstDetectedLocal.DayNumber + 1;

    private static int Rank(string severity) => severity switch
    {
        "high" => 3,
        "notable" => 2,
        _ => 1,
    };
}
