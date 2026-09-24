using Vitara.Domain.Entities;

namespace Vitara.Insight.Health;

// Did the illness detector actually work?
//
// Every threshold in this system was chosen by argument. That is fine for the ones
// nobody can check, but the early-illness signal has ground truth sitting right there
// and unused: the user marks the days they were ill, so a baseline is not corrupted by
// a week of fever. Those marks are exactly the labels an evaluation needs, and without
// something like this the detector's thresholds can only ever be defended, never
// tested -- which is how a detector ends up tuned to whatever produced a pleasing
// number of alerts in the first month.
//
// The measurements are the three that matter for a detector meant to warn you:
//
//   caught     -- an illness with a signal running into it or during it
//   lead       -- how many days before the illness was marked the signal started.
//                 Positive is a warning; zero or negative is a confirmation, which is
//                 worth much less and should not be reported as the same thing
//   false alarms -- runs of signal with no illness anywhere near them. The number that
//                 decides whether the detector survives contact with a real user
//
// Nothing here is a judgement about health. It is a judgement about a detector, and it
// is deliberately unflattering: a small sample says so rather than producing a
// percentage that looks like a result.
public static class IllnessEval
{
    // How far before an illness is marked a signal still counts as having caught it.
    // Beyond about a week it is not a warning about this illness, it is a coincidence.
    public const int LeadWindowDays = 7;

    // Illness is marked by hand and rarely on the first bad morning; a signal still
    // running just after the marked end is the same episode, not a new false alarm.
    public const int GraceDays = 3;

    public record Episode(DateOnly Start, DateOnly End, bool Caught, int? LeadDays, DateOnly? SignalStart, string? Notes);

    public record Run(DateOnly Start, DateOnly End);

    public record Result(
        int Episodes,
        int Caught,
        int Missed,
        int FalseAlarms,
        int DaysEvaluated,
        double? MedianLeadDays,
        IReadOnlyList<Episode> PerEpisode,
        IReadOnlyList<Run> FalseAlarmRuns,
        string Verdict);

    public static Result Evaluate(
        IReadOnlyList<DailyVitals> vitals,
        IReadOnlyList<ExcludedPeriod> periods,
        HealthThresholdSet thresholds,
        int sustainedDays)
    {
        var illness = periods
            .Where(p => p.Reason == "illness")
            .OrderBy(p => p.StartLocal)
            .ToList();

        var ordered = vitals.OrderBy(v => v.Day).ToList();
        var signalDays = SignalDays(ordered, thresholds, sustainedDays);
        var runs = Group(signalDays);

        var episodes = illness.Select(p =>
        {
            // The first run that starts within the lead window and no later than the
            // illness ended. A run beginning after recovery is not a catch.
            var hit = runs.FirstOrDefault(r =>
                r.Start >= p.StartLocal.AddDays(-LeadWindowDays) && r.Start <= p.EndLocal);

            return new Episode(
                p.StartLocal,
                p.EndLocal,
                hit is not null,
                hit is null ? null : p.StartLocal.DayNumber - hit.Start.DayNumber,
                hit?.Start,
                p.Notes);
        }).ToList();

        var falseAlarms = runs.Where(r => !illness.Any(p =>
                r.Start <= p.EndLocal.AddDays(GraceDays) &&
                r.End >= p.StartLocal.AddDays(-LeadWindowDays)))
            .ToList();

        var leads = episodes.Where(e => e.LeadDays is not null).Select(e => (double)e.LeadDays!.Value).OrderBy(x => x).ToList();
        var medianLead = leads.Count == 0
            ? (double?)null
            : leads.Count % 2 == 1
                ? leads[leads.Count / 2]
                : (leads[leads.Count / 2 - 1] + leads[leads.Count / 2]) / 2;

        var caught = episodes.Count(e => e.Caught);

        return new Result(
            episodes.Count,
            caught,
            episodes.Count - caught,
            falseAlarms.Count,
            ordered.Count,
            medianLead,
            episodes,
            falseAlarms,
            Verdict(episodes.Count, caught, falseAlarms.Count, medianLead, ordered.Count));
    }

    // Every day the detector would have spoken, replayed through the detector itself
    // rather than through a copy of its rule -- a copy would drift and then the
    // evaluation would be measuring something nobody ships.
    private static List<DateOnly> SignalDays(
        IReadOnlyList<DailyVitals> ordered, HealthThresholdSet thresholds, int sustainedDays)
    {
        var days = new List<DateOnly>();

        for (var i = sustainedDays - 1; i < ordered.Count; i++)
        {
            var upToHere = ordered.Take(i + 1).ToList();
            if (FindingDetectors.EarlyIllness(upToHere, thresholds, sustainedDays) is not null)
                days.Add(ordered[i].Day);
        }

        return days;
    }

    // Consecutive days are one episode. A signal that stops for a day and returns is
    // two runs by this count, which is the harsher reading and the right one: the user
    // was told twice.
    private static List<Run> Group(IReadOnlyList<DateOnly> days)
    {
        var runs = new List<Run>();
        if (days.Count == 0) return runs;

        var start = days[0];
        var previous = days[0];

        foreach (var day in days.Skip(1))
        {
            if (day.DayNumber - previous.DayNumber > 1)
            {
                runs.Add(new Run(start, previous));
                start = day;
            }

            previous = day;
        }

        runs.Add(new Run(start, previous));
        return runs;
    }

    // Said in words, and pessimistically. Three episodes is not an evaluation, and a
    // percentage computed from two of them would be read as one.
    private static string Verdict(int episodes, int caught, int falseAlarms, double? medianLead, int days)
    {
        if (days == 0) return "Nothing to evaluate: no vitals in the window.";

        if (episodes == 0)
            return "No illness has been marked, so there is nothing to check the detector against. " +
                   "Mark the days you were ill and this becomes an answer rather than a blank.";

        var sample = episodes < 3
            ? $"Only {episodes} marked {(episodes == 1 ? "illness" : "illnesses")} in {days} days of readings, " +
              "which is too few to conclude anything. Treat what follows as an anecdote."
            : $"{episodes} marked illnesses over {days} days of readings.";

        var hit = $"{caught} of {episodes} had a signal running into them.";

        var lead = medianLead switch
        {
            null => "None were warned about in advance.",
            > 0 => $"Median warning was {medianLead:0.#} days before the illness was marked.",
            0 => "The signal arrived the same day the illness was marked, which is confirmation rather than warning.",
            _ => $"The signal arrived {Math.Abs(medianLead.Value):0.#} days after the illness was marked, which is " +
                 "confirmation rather than warning.",
        };

        var noise = falseAlarms switch
        {
            0 => "No signal ran outside a marked illness.",
            1 => "One signal ran with no illness near it.",
            _ => $"{falseAlarms} signals ran with no illness near them, which is the number that decides " +
                 "whether this detector is worth keeping.",
        };

        return $"{sample} {hit} {lead} {noise}";
    }
}
