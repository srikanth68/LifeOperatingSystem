namespace Vitara.Application.Health;

// A detected step to a new level.
public record RegimeChange(DateOnly ChangePointLocal, double Before, double After, double ShiftInSigmas, int DaysHeld)
{
    public string Direction => After > Before ? "up" : "down";
}

// Finding the point where a metric stopped being what it was.
//
// A sustained step change is not a deviation to be averaged away -- it is a new normal,
// and a rolling baseline that absorbs it over sixty days ends up describing neither the
// old level nor the new one, while never mentioning that anything happened.
//
// A two-window mean-shift test is enough. There is no case here for anything cleverer:
// the signal is a step in a noisy series, the series is short, and a method nobody can
// check by hand produces conclusions nobody can argue with.
//
// THE DWELL REQUIREMENT IS THE IMPORTANT PART. The spec says a detected change closes
// the old baseline and opens a new one. Without requiring the new level to persist,
// a twitchy detector resets on every wobble -- and once "normal" is redefined every
// few days, nothing is ever abnormal again. The detector would quietly disable the
// entire system it feeds, and it would look like it was working the whole time.
public static class RegimeDetector
{
    // Finds the most recent qualifying step change, or null.
    //
    // Most recent rather than largest: the question being asked is "what is normal
    // now", and an older change has already been absorbed into how things are.
    public static RegimeChange? Detect(
        IReadOnlyList<(DateOnly Day, double Value)> series,
        double shiftInSigmas,
        int dwellDays,
        int minBefore = 14)
    {
        if (series.Count < minBefore + dwellDays) return null;

        var ordered = series.OrderBy(p => p.Day).ToList();

        // Walk backwards so the first qualifying split found is the most recent one.
        // The candidate must leave enough history behind it to say what the old level
        // was, and enough after it to prove the new level held.
        for (var split = ordered.Count - dwellDays; split >= minBefore; split--)
        {
            var before = ordered.Take(split).Select(p => p.Value).ToList();
            var after = ordered.Skip(split).Select(p => p.Value).ToList();

            // Robust statistics on the before-window: a couple of odd days ahead of a
            // real change should not decide whether the change is visible.
            var beforeCentre = Statistics.Median(before);
            var spread = Statistics.MedianAbsoluteDeviation(before);

            var afterCentre = Statistics.Median(after);

            // Pooled, so a metric that is steady before a change and noisy after it is
            // judged against the larger of the two rather than the flattering one.
            var pooled = Math.Max(spread, Statistics.MedianAbsoluteDeviation(after));

            // A metric with no variance at all -- a low-resolution score that sits on
            // one value for weeks -- has no scale to express a shift in. The sigma test
            // is meaningless there rather than failed, so it is skipped and the
            // separation test below decides on its own. Two constant windows at
            // different levels are a step change by any reading.
            var degenerate = pooled <= 1e-9;
            var shift = degenerate ? double.MaxValue : Math.Abs(afterCentre - beforeCentre) / pooled;

            if (!degenerate && shift < shiftInSigmas) continue;

            // The sigma test alone is not enough on a very steady metric. When a series
            // barely moves, its MAD shrinks towards zero and an utterly trivial
            // difference in medians -- half a beat per minute, pure noise -- reads as
            // several sigmas. Left there, the detector reclassifies noise as a new
            // normal and resets the baseline constantly.
            //
            // So the two windows must also actually SEPARATE: the bulk of one sits
            // clear of the bulk of the other. A real step change does this trivially;
            // noise never does, however small the spread happens to be.
            var separated = afterCentre > beforeCentre
                ? Statistics.Percentile(before, 0.75) < Statistics.Percentile(after, 0.25)
                : Statistics.Percentile(before, 0.25) > Statistics.Percentile(after, 0.75);

            if (!separated) continue;

            // Held for long enough to be a level rather than an episode. A fortnight of
            // illness is a step change that reverts, and calling it a new normal would
            // reset the baseline to the illness.
            var daysHeld = ordered[^1].Day.DayNumber - ordered[split].Day.DayNumber + 1;
            if (daysHeld < dwellDays) continue;

            // Capped so the stored figure stays a number. The degenerate case above
            // produces an arbitrarily large one, and a serialised infinity in an
            // evidence blob is a problem for whatever reads it later.
            return new RegimeChange(ordered[split].Day, beforeCentre, afterCentre, Math.Min(shift, 999), daysHeld);
        }

        return null;
    }

    // What might explain it. An unexplained regime change is more interesting than an
    // explained one, not less -- so this returns what overlapped, and the caller emits
    // a finding either way.
    public static string? Attribute(
        DateOnly changePoint,
        IEnumerable<(DateOnly Start, DateOnly? End, string Description)> candidates,
        int windowDays = 7)
    {
        // A medication started a few days before the level moved is the explanation;
        // requiring the dates to match exactly would attribute almost nothing.
        var from = changePoint.AddDays(-windowDays);
        var to = changePoint.AddDays(windowDays);

        var hits = candidates
            .Where(c => c.Start <= to && (c.End is null || c.End >= from))
            .Select(c => c.Description)
            .ToList();

        return hits.Count == 0 ? null : string.Join("; ", hits);
    }
}
