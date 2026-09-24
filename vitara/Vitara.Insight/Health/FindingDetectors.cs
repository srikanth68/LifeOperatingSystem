using System.Text.Json;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.Insight.Health;

// One day's z-scores for the three metrics the illness signal reads.
//
// Skin temperature carries its raw value too, in degrees. It is the only one of the
// three that arrives already baselined by the ring, and a z-score of a deviation can
// be large when the movement is trivial or small when the movement is real -- see
// HealthThresholds.IllnessTempFloorC for both failure modes. The degrees are what
// bracket it.
public record DailyVitals(
    DateOnly Day,
    double? RestingHrZ,
    double? HrvZ,
    double? SkinTempZ,
    double? SkinTempC = null);

// The deterministic half of the system: what was detected, computed in code.
//
// Nothing here asks the model anything. It is shown these findings afterwards and
// asked what is worth saying -- it is never asked what happened, and it never
// generates the identity key, because a key that changes between runs cannot be
// deduplicated, cooled down, or resolved, and the same condition would arrive as a
// fresh notification every single morning.
public static class FindingDetectors
{
    // Stable across runs for the same condition. Content-derived, in code.
    private static string Key(string type, string metric, string direction) => $"{type}:{metric}:{direction}";

    // ── Deviation ───────────────────────────────────────────────────────────────
    // Outside personal normal, and still outside it tomorrow. The sustained
    // requirement is the cheapest and most effective noise filter available: a single
    // odd night is a single odd night, and a system that says so every time is one
    // that gets muted.
    public static Finding? Deviation(
        string metric, IReadOnlyList<(DateOnly Day, double Z)> recentZ, double threshold, int sustainedDays)
    {
        if (recentZ.Count < sustainedDays) return null;

        var window = recentZ.OrderByDescending(p => p.Day).Take(sustainedDays).ToList();

        // All of the window has to breach, in the SAME direction. A metric bouncing
        // above and below is unsettled, not deviating, and calling that a finding
        // describes the noise rather than the person.
        var high = window.All(p => p.Z >= threshold);
        var low = window.All(p => p.Z <= -threshold);
        if (!high && !low) return null;

        var direction = high ? "high" : "low";
        var latest = window[0];

        return new Finding
        {
            Key = Key(FindingTypes.Deviation, metric, direction),
            Type = FindingTypes.Deviation,
            Metric = metric,
            Direction = direction,
            Severity = Math.Abs(latest.Z) >= threshold * 1.75 ? "notable" : "info",
            Summary = $"{metric} has been {direction} against your baseline for {sustainedDays} days " +
                      $"({latest.Z:+0.0;-0.0} SD).",
            EvidenceJson = JsonSerializer.Serialize(new { z = window.Select(w => Math.Round(w.Z, 2)), threshold }),
            FirstDetectedLocal = window[^1].Day,
            LastDetectedLocal = latest.Day,
        };
    }

    // ── Early illness ───────────────────────────────────────────────────────────
    // Resting heart rate up, HRV down, skin temperature up. The most valuable detector
    // here and the one most able to destroy trust in everything around it: fire it
    // spuriously twice and it stops being read, including on the morning it is right.
    //
    // Two of three, sustained. Not one of three -- any single one of these moves for
    // a late meal, a hard session, a warm room.
    public static Finding? EarlyIllness(
        IReadOnlyList<DailyVitals> recent, HealthThresholdSet t, int sustainedDays)
    {
        if (recent.Count < sustainedDays) return null;

        var window = recent.OrderByDescending(v => v.Day).Take(sustainedDays).ToList();

        // Each day must independently show at least two of the three. A day where
        // resting HR is up and a different day where HRV is down is not a signal --
        // it is two unrelated days.
        var perDay = window.Select(v => new
        {
            v.Day,
            Hits = (v.RestingHrZ >= t.IllnessRestingHrZ ? 1 : 0)
                 + (v.HrvZ <= t.IllnessHrvZ ? 1 : 0)
                 + (TemperatureCounts(v.SkinTempZ, v.SkinTempC, t) ? 1 : 0),
        }).ToList();

        if (perDay.Any(d => d.Hits < 2)) return null;

        var allThree = perDay.All(d => d.Hits == 3);
        var latest = window[0];

        return new Finding
        {
            Key = Key(FindingTypes.EarlyIllness, "vitals", "elevated"),
            Type = FindingTypes.EarlyIllness,
            Metric = "vitals",
            Direction = "elevated",
            Severity = allThree ? "high" : "notable",
            // Confidence, not certainty. Three of three is a stronger reading of the
            // same evidence, never a diagnosis.
            Confidence = allThree ? 0.8 : 0.55,
            Summary = allThree
                ? $"Resting heart rate, HRV and skin temperature have all moved the way they do before illness, {sustainedDays} days running."
                : $"Two of resting heart rate, HRV and skin temperature have moved the way they do before illness, {sustainedDays} days running.",
            EvidenceJson = JsonSerializer.Serialize(new
            {
                days = window.Select(v => new
                {
                    day = v.Day.ToString("yyyy-MM-dd"),
                    restingHrZ = Round(v.RestingHrZ),
                    hrvZ = Round(v.HrvZ),
                    skinTempZ = Round(v.SkinTempZ),

                    // In degrees as well as in standard deviations, because the two can
                    // disagree and the reader deserves to see which one fired.
                    skinTempC = Round(v.SkinTempC),
                    skinTempCounted = TemperatureCounts(v.SkinTempZ, v.SkinTempC, t),
                }),
            }),
            FirstDetectedLocal = window[^1].Day,
            LastDetectedLocal = latest.Day,
        };
    }

    // ── Regime change ───────────────────────────────────────────────────────────
    // A finding either way. An unexplained step change is the MORE interesting one --
    // an explained one has already been accounted for by the thing that explains it.
    // adopted says whether the baseline was rebuilt around the new level. An
    // unexplained step in the harmful direction is NOT adopted (see RegimeDecision),
    // and that is the loudest version of this finding: the level moved the wrong way,
    // nothing accounts for it, and the system is still holding the old normal.
    public static Finding FromRegimeChange(
        string metric, RegimeChange change, string? attribution, DateOnly today, bool adopted = true)
    {
        var moved = $"{metric} settled at a new level around {change.ChangePointLocal:d MMM} " +
                    $"({Math.Round(change.Before, 1)} → {Math.Round(change.After, 1)}), held for {change.DaysHeld} days";

        return new Finding
        {
            Key = Key(FindingTypes.RegimeChange, metric, change.Direction),
            Type = FindingTypes.RegimeChange,
            Metric = metric,
            Direction = change.Direction,
            Severity = attribution is not null ? "info" : adopted ? "notable" : "high",
            Summary = attribution is not null
                ? $"{moved}, which lines up with: {attribution}."
                : adopted
                    ? $"{moved}. Nothing recorded explains it."
                    : $"{moved}, the wrong way, and nothing recorded explains it. Your normal has been left where " +
                      $"it was rather than rebuilt around the new level, so this stays visible until something accounts for it.",
            EvidenceJson = JsonSerializer.Serialize(new
            {
                before = Math.Round(change.Before, 2),
                after = Math.Round(change.After, 2),
                shiftInSigmas = Math.Round(change.ShiftInSigmas, 2),
                change.DaysHeld,
                attribution,
                adopted,
            }),
            FirstDetectedLocal = change.ChangePointLocal,
            LastDetectedLocal = today,
        };
    }

    // ── Strain ──────────────────────────────────────────────────────────────────
    public static Finding? Strain(double? acwr, double lowBound, double highBound, DateOnly today)
    {
        if (acwr is not { } ratio) return null;
        if (ratio >= lowBound && ratio <= highBound) return null;

        var direction = ratio > highBound ? "high" : "low";

        return new Finding
        {
            Key = Key(FindingTypes.StrainRisk, "acwr", direction),
            Type = FindingTypes.StrainRisk,
            Metric = "acwr",
            Direction = direction,
            // Never above "info". The acute-to-chronic ratio has a weaker evidence base
            // than its popularity suggests -- numerator and denominator share data, and
            // the threshold bands have not held up well to scrutiny. It is worth
            // showing and not worth alarming anyone about.
            Severity = "info",
            Summary = direction == "high"
                ? $"Your recent training load is {ratio:0.0}x your longer-term average — ramping faster than usual."
                : $"Your recent training load is {ratio:0.0}x your longer-term average — well below your usual.",
            EvidenceJson = JsonSerializer.Serialize(new { acwr = Math.Round(ratio, 2), lowBound, highBound }),
            FirstDetectedLocal = today,
            LastDetectedLocal = today,
        };
    }

    public static Finding? SleepDebtFinding(double debtMinutes, double thresholdMinutes, string basis, DateOnly today)
    {
        if (debtMinutes < thresholdMinutes) return null;

        return new Finding
        {
            Key = Key(FindingTypes.StrainRisk, "sleep_debt", "high"),
            Type = FindingTypes.StrainRisk,
            Metric = "sleep_debt",
            Direction = "high",
            Severity = "notable",
            Summary = $"You are about {debtMinutes / 60:0.0} hours short on sleep over the past fortnight.",
            // The basis travels with the number. "Four hours short" means something
            // different when the need it is measured against was inferred from the data
            // rather than stated by the user, and the reader deserves to know which.
            EvidenceJson = JsonSerializer.Serialize(new { debtMinutes = Math.Round(debtMinutes), basis }),
            FirstDetectedLocal = today,
            LastDetectedLocal = today,
        };
    }

    // ── Drift ───────────────────────────────────────────────────────────────────
    // Only ever emitted for a trend that passed the significance test. A slope always
    // exists; fit a line to noise and a line comes back. Without that gate this would
    // announce a discovery most days.
    public static Finding? Drift(string metric, Statistics.TrendResult trend, int windowDays, DateOnly today)
    {
        if (!trend.IsSignificant) return null;

        var direction = trend.SlopePerDay > 0 ? "rising" : "falling";
        var perMonth = trend.SlopePerDay * 30;

        return new Finding
        {
            Key = Key(FindingTypes.Drift, metric, direction),
            Type = FindingTypes.Drift,
            Metric = metric,
            Direction = direction,
            Severity = "info",
            Summary = $"{metric} has been {direction} steadily for {windowDays} days — about {Math.Abs(perMonth):0.##} per month.",
            EvidenceJson = JsonSerializer.Serialize(new
            {
                slopePerDay = Math.Round(trend.SlopePerDay, 4),
                perMonth = Math.Round(perMonth, 3),
                mannKendallZ = Math.Round(trend.Z, 2),
                n = trend.N,
                windowDays,
            }),
            FirstDetectedLocal = today.AddDays(-windowDays),
            LastDetectedLocal = today,
        };
    }

    // ── Staleness ───────────────────────────────────────────────────────────────
    // Missing data is a finding, not a gap to paper over. A ring left in a drawer
    // produces the same silence as a week of perfect health, and only one of those is
    // worth saying nothing about.
    public static Finding? Staleness(string metric, DateOnly? lastSeen, DateOnly today, int allowedDays)
    {
        var daysMissing = lastSeen is null ? int.MaxValue : today.DayNumber - lastSeen.Value.DayNumber;
        if (daysMissing <= allowedDays) return null;

        return new Finding
        {
            Key = Key(FindingTypes.Staleness, metric, "missing"),
            Type = FindingTypes.Staleness,
            Metric = metric,
            Direction = "missing",
            Severity = "info",
            Summary = lastSeen is null
                ? $"No {metric} has ever been recorded."
                : $"No {metric} recorded since {lastSeen:d MMM} — {daysMissing} days.",
            EvidenceJson = JsonSerializer.Serialize(new { lastSeen = lastSeen?.ToString("yyyy-MM-dd"), daysMissing, allowedDays }),
            FirstDetectedLocal = lastSeen?.AddDays(allowedDays) ?? today,
            LastDetectedLocal = today,
        };
    }

    // Whether the temperature component counts toward the illness signal today.
    //
    // Unusual AND actually warm, or warm enough that unusual stops mattering. A ring
    // that reports no degrees at all falls back to the z-score alone -- an imported
    // history without raw values should still be readable, and refusing to score it
    // would silently drop the component rather than say anything.
    public static bool TemperatureCounts(double? z, double? degrees, HealthThresholdSet t)
    {
        if (degrees is { } warm && warm >= t.IllnessTempOverrideC) return true;
        if (z is not { } score || score < t.IllnessTempZ) return false;

        return degrees is null || degrees.Value >= t.IllnessTempFloorC;
    }

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 2);
}

// The thresholds, passed in rather than read from the environment inside the
// detectors, so a test can state the numbers it is testing against instead of
// mutating process state.
public record HealthThresholdSet(
    double IllnessRestingHrZ,
    double IllnessHrvZ,
    double IllnessTempZ,
    // The two absolutes that bracket the skin-temperature z-score, in degrees. Given
    // defaults so a test that only cares about the three z-scores can still state them
    // and nothing else.
    double IllnessTempFloorC = 0.15,
    double IllnessTempOverrideC = 0.50)
{
    public static HealthThresholdSet FromConfiguration() => new(
        HealthThresholds.IllnessRestingHrZ,
        HealthThresholds.IllnessHrvZ,
        HealthThresholds.IllnessTempZ,
        HealthThresholds.IllnessTempFloorC,
        HealthThresholds.IllnessTempOverrideC);
}
