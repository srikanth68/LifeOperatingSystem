using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// Whether a detected step change becomes the new normal, and what might explain it.
//
// Detection and adoption used to be the same act: anything that stepped and held reset
// the baseline. That is right for a medication and wrong for a decline. A slow-onset
// illness, an overtraining spiral or metabolic drift all look exactly like a sustained
// step change, and adopting one rebuilds "normal" around the worse number -- after
// which every deviation check agrees that nothing is wrong.
//
// So the two mechanisms are arbitrated here. A step is adopted when something recorded
// explains it, or when it is not in the harmful direction for that metric. An
// unexplained step the wrong way is reported and NOT adopted: the baseline stays where
// it was, and the finding keeps saying so until something accounts for it.
public static class RegimeDecision
{
    // A medication started a few days before the level moved is the explanation;
    // requiring the dates to line up exactly would attribute almost nothing.
    public const int AttributionWindowDays = 7;

    public static string? Explain(
        DateOnly changePoint,
        IReadOnlyList<Intervention>? interventions = null,
        IReadOnlyList<ExcludedPeriod>? excluded = null,
        IReadOnlyList<TravelPeriod>? travel = null,
        IReadOnlyList<Device>? devices = null)
    {
        var from = changePoint.AddDays(-AttributionWindowDays);
        var to = changePoint.AddDays(AttributionWindowDays);
        var reasons = new List<string>();

        foreach (var i in interventions ?? [])
        {
            if (Overlaps(i.StartedOnLocal, i.EndedOnLocal, from, to))
                reasons.Add($"{i.Kind} {i.Name}".Trim());
        }

        foreach (var d in devices ?? [])
        {
            // Only the start of a device's life explains a step: that is the day the
            // instrument changed.
            if (d.ActiveFromLocal >= from && d.ActiveFromLocal <= to)
                reasons.Add($"a new {d.Kind}{(string.IsNullOrWhiteSpace(d.Model) ? "" : $" ({d.Model})")}");
        }

        foreach (var e in excluded ?? [])
        {
            if (Overlaps(e.StartLocal, e.EndLocal, from, to))
                reasons.Add(e.Reason.Replace('_', ' '));
        }

        foreach (var t in travel ?? [])
        {
            if (Overlaps(t.StartLocal, t.EndLocal, from, to))
                reasons.Add("travel");
        }

        return reasons.Count == 0 ? null : string.Join(", ", reasons.Distinct());
    }

    // Adopted = the baseline is rebuilt from the change point. Explained changes and
    // changes in the harmless direction are adopted; an unexplained adverse one is not.
    public static bool ShouldAdopt(string metric, string direction, string? explanation) =>
        explanation is not null || !MetricDirection.IsAdverse(metric, direction);

    private static bool Overlaps(DateOnly start, DateOnly? end, DateOnly from, DateOnly to) =>
        start <= to && (end is null || end >= from);
}
