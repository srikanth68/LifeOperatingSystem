using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// What to take to a doctor.
//
// This is the closest thing here to the "AI doctor" everybody wants, and it is
// deliberately not one. The useful observation is that the hard part of a ten-minute
// appointment is not diagnosis — it is that the patient cannot remember when the
// headaches started, does not know their resting heart rate has climbed four beats
// since spring, and leaves without asking the thing they came in for. Every one of
// those is a recall problem, and a system holding two years of daily measurements is
// extremely good at recall.
//
// So: no diagnosis, no treatment, no "you probably have". What it produces is a sheet —
// what changed, what is outside a printed range, how long each has been true, and the
// questions those facts raise. A clinician reads it in thirty seconds and asks better
// questions than they could have otherwise.
//
// THE REFUSALS ARE THE DESIGN. Three in particular:
//
//   1. It never names a condition. "Resting heart rate up 4 bpm over six weeks" is a
//      measurement. "You might have a thyroid problem" is a diagnosis, and the gap
//      between those two is a medical degree.
//   2. It says what it cannot see. No symptoms, no medications, no family history, no
//      examination, no imaging — a brief that reads as complete is more dangerous than
//      no brief at all, because it invites the reader to treat absence as reassurance.
//   3. It says what has no normal yet. "Nothing is flagged" over eleven settled metrics
//      and thirty unsettled ones is a sentence that misleads by being true.
public static class VisitBrief
{
    // One line for the sheet: what, how long, and what it raises.
    public record Item(
        string Topic,
        string What,
        string? Since,
        string Severity,          // info | notable | urgent-ish is NOT a tier here; see Urgency
        string? Ask,
        LabReading? Lab = null);  // the numbers behind a lab sentence, so it can be drawn

    public record Result(
        string Scope,
        string Verdict,
        IReadOnlyList<Item> Bring,
        IReadOnlyList<string> Questions,
        IReadOnlyList<string> NotLookedAt,
        string Coverage,
        string Disclaimer);

    public const string Disclaimer =
        "Prepared from consumer wearable readings and lab values you entered. It is a record, not an " +
        "assessment: this is not a diagnosis and it is not advice about treatment, and a measurement " +
        "outside a reference range is a question for a clinician rather than an answer.";

    // The things this system structurally cannot know, said out loud. A brief that
    // looks complete invites its absences to be read as reassurance.
    private static readonly string[] Blind =
    [
        "symptoms, and how you actually feel",
        "medications, supplements and doses",
        "family history",
        "anything from a physical examination",
        "imaging, biopsies and anything a specialist has seen",
        "mental health",
        "alcohol, smoking and recreational drugs, unless you logged them",
    ];

    public static Result Build(
        IReadOnlyList<Finding> active,
        IReadOnlyList<Measurement> labResults,
        IReadOnlyList<LabPanel> panels,
        IReadOnlyList<ReferenceRange> ranges,
        IReadOnlyList<Baseline> baselines,
        string? biologicalSex,
        int? age,
        DateOnly today)
    {
        var bring = new List<Item>();
        var questions = new List<string>();

        // ── What the detectors are currently saying ─────────────────────────────
        //
        // Ordered by how long each has been true, not by severity. A clinician reading
        // this wants duration first: three mornings is a bad week, forty is a pattern,
        // and the forty-day one is the one worth the appointment.
        foreach (var f in active
                     .OrderByDescending(f => f.LastDetectedLocal.DayNumber - f.FirstDetectedLocal.DayNumber)
                     .ThenByDescending(f => Rank(f.Severity)))
        {
            var days = f.LastDetectedLocal.DayNumber - f.FirstDetectedLocal.DayNumber + 1;
            var since = days <= 1
                ? "first seen today"
                : $"{days} days, since {f.FirstDetectedLocal:d MMM}";

            bring.Add(new Item(
                MetricCatalogue.Find(f.Metric)?.Label ?? Words(f.Metric),
                f.Summary,
                since,
                f.Severity,
                AskFor(f, days),
                f.Type == FindingTypes.LabAnchor ? LabReading.FromEvidence(f.EvidenceJson) : null));
        }

        // ── Blood work, against the printed range ───────────────────────────────
        var latestPanel = panels.OrderByDescending(p => p.DrawnOnLocal).FirstOrDefault();
        if (latestPanel is not null)
        {
            var rows = labResults.Where(m => m.LabPanelId == latestPanel.Id).ToList();

            // A lab the detector has already flagged is on the sheet once, as the finding,
            // which carries how long it has been running. Listing the same draw again
            // underneath read as two separate problems. Matched on the draw date as well as
            // the metric: a finding about an OLDER draw says nothing about this one.
            var alreadyFlagged = active
                .Where(f => f.Type == FindingTypes.LabAnchor && f.FirstDetectedLocal == latestPanel.DrawnOnLocal)
                .Select(f => f.Metric)
                .ToHashSet();

            // Counted before the duplicates are skipped, so that dropping one can never turn
            // into the sentence below claiming the whole panel was in range.
            var outOfRange = 0;

            foreach (var row in rows)
            {
                var range = ReferenceRanges.For(ranges, row.Metric, biologicalSex, age, latestPanel.LabName);
                var standing = ReferenceRanges.Where(row.Value, range);
                if (standing is not (ReferenceRanges.Standing.Below or ReferenceRanges.Standing.Above)) continue;

                outOfRange++;
                if (alreadyFlagged.Contains(row.Metric)) continue;

                var label = MetricCatalogue.Find(row.Metric)?.Label ?? row.Metric;

                bring.Add(new Item(
                    label,
                    $"{label} {row.Value:0.##}{(string.IsNullOrWhiteSpace(row.Unit) ? "" : " " + row.Unit)}, " +
                    ReferenceRanges.Describe(standing, range) + ".",
                    $"drawn {latestPanel.DrawnOnLocal:d MMM yyyy}",
                    "notable",
                    $"Is this {label.ToLowerInvariant()} worth repeating or acting on?",
                    range is null || (range.Low is null && range.High is null)
                        ? null
                        : new LabReading(row.Value, null, range.Low, range.High,
                            string.IsNullOrWhiteSpace(row.Unit) ? range.Unit : row.Unit,
                            latestPanel.DrawnOnLocal.ToString("yyyy-MM-dd"),
                            standing == ReferenceRanges.Standing.Below ? "below" : "above")));
            }

            if (rows.Count > 0 && outOfRange == 0)
                questions.Add($"Everything on the {latestPanel.DrawnOnLocal:d MMM} panel sat inside its reference range " +
                              "— is there anything on it you would want repeated anyway?");
        }
        else
        {
            questions.Add("There is no blood work recorded here at all. Is any worth doing?");
        }

        // ── The questions those facts raise ─────────────────────────────────────
        questions.AddRange(bring.Select(b => b.Ask).Where(a => a is not null)!.Cast<string>());

        // Always, regardless of what was found. The appointment is the point.
        questions.Add("Given all of this, is there anything you would want to measure that I am not measuring?");

        var settled = baselines.Count(b => b.IsValid);
        var learning = baselines.Count(b => !b.IsValid);

        var coverage = learning == 0
            ? $"{settled} measurements have enough history to be compared against."
            : $"{settled} measurements have enough history to be compared against; {learning} do not yet, " +
              "so nothing has been checked for those and their absence from this sheet means nothing.";

        var verdict = bring.Count switch
        {
            0 => "Nothing is currently outside this person's own usual range or a printed reference range. " +
                 "That is not the same as nothing being wrong — see what is not looked at, below.",
            1 => "One thing is worth raising.",
            _ => $"{bring.Count} things are worth raising, longest-running first.",
        };

        return new Result(
            Scope: "A record of measurements over time, prepared for a consultation.",
            Verdict: verdict,
            Bring: bring,
            Questions: questions.Distinct().ToList(),
            NotLookedAt: Blind,
            Coverage: coverage,
            Disclaimer: Disclaimer);
    }

    // A finding about something that is not a catalogued measurement (sleep debt, a composite
    // pattern) has no label to borrow, and its key is a column name. This sheet is read by a
    // clinician, so the key is turned into words rather than printed as it is stored.
    private static string Words(string key)
    {
        var text = key.Replace('_', ' ').Trim();
        return text.Length == 0 ? key : char.ToUpperInvariant(text[0]) + text[1..];
    }

    // The question a finding raises, written as the patient would ask it. Templated
    // from the finding type rather than generated, because a model writing these would
    // eventually write one that reads as a diagnosis.
    private static string? AskFor(Finding f, int days) => f.Type switch
    {
        FindingTypes.EarlyIllness =>
            "Several overnight measures moved together the way they do before an illness. Does that fit anything you see?",

        FindingTypes.Drift when days >= 28 =>
            $"{Label(f)} has been drifting for {days} days rather than jumping. Is a slow change like that worth investigating?",

        FindingTypes.Drift => null,

        FindingTypes.RegimeChange =>
            $"{Label(f)} settled at a new level and stayed there. Is that worth explaining?",

        FindingTypes.LabAnchor =>
            $"Is this {Label(f).ToLowerInvariant()} result worth repeating or acting on?",

        FindingTypes.Deviation when days >= 7 =>
            $"{Label(f)} has been away from my usual for {days} days. Should that be looked at?",

        FindingTypes.StrainRisk =>
            "My training load has been above the range where injuries stay rare. Is that a problem at my age and history?",

        _ => null,
    };

    private static string Label(Finding f) => MetricCatalogue.Find(f.Metric)?.Label ?? f.Metric;

    private static int Rank(string severity) => severity switch
    {
        "high" => 3,
        "notable" => 2,
        _ => 1,
    };
}
