namespace Vitara.Domain.Entities;

// Something Vitara concluded, computed in code.
//
// Never produced by the model. The model is shown findings and asked what is worth
// saying; it is not asked what happened. That division is the whole architecture, and
// it is also why the identity key below is derived from content in code -- a model
// asked to generate a key produces a different one every run for the same condition,
// and then nothing can be deduplicated, cooled down, or resolved.
public class Finding
{
    public long Id { get; set; }

    // Stable across runs for the same condition: type + metric + direction. This is
    // what the ledger deduplicates on, so the same elevated resting heart rate on four
    // consecutive mornings is one ongoing finding rather than four notifications.
    public string Key { get; set; } = "";

    public string Type { get; set; } = "";           // see FindingTypes
    public string Metric { get; set; } = "";
    public string Direction { get; set; } = "";      // high | low | rising | falling | missing

    public string Severity { get; set; } = "info";   // info | notable | high
    public double? Confidence { get; set; }          // 0-1, where the detector supports it

    // Plain statement of what was detected. Deterministic text, not model output -- the
    // model rewrites this for the user, but the record stays literal.
    public string Summary { get; set; } = "";

    // The numbers behind it: values, baseline, z-scores, how many days. Kept so a
    // finding can be re-examined later, and so the model can be given evidence rather
    // than being asked to trust a sentence.
    public string? EvidenceJson { get; set; }

    // A finding persists while the condition does. First and last are what let it be
    // reported as "for the fourth day" instead of as something new every morning.
    public DateOnly FirstDetectedLocal { get; set; }
    public DateOnly LastDetectedLocal { get; set; }
    public DateOnly? ResolvedLocal { get; set; }

    public bool IsActive => ResolvedLocal is null;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class FindingTypes
{
    // Outside the personal baseline, sustained. Single-day noise does not speak.
    public const string Deviation = "deviation";

    // Resting HR up, HRV down, skin temperature up. The most valuable detector here
    // and the one most able to destroy trust in the whole feature by crying wolf.
    public const string EarlyIllness = "early_illness";

    // A sustained step to a new level, with attribution attempted.
    public const string RegimeChange = "regime_change";

    // Acute load against chronic load, or accumulated sleep debt.
    public const string StrainRisk = "strain_risk";

    // Slow directional movement, invisible day to day.
    public const string Drift = "drift";

    // A lab value outside its range, or materially moved from the last draw.
    public const string LabAnchor = "lab_anchor";

    // Expected data that did not arrive. Missing data is a finding, not a gap to paper
    // over: a ring left in a drawer looks exactly like a week of perfect health.
    public const string Staleness = "staleness";
}

// Every number that decides whether a finding fires.
//
// Gathered in one place and overridable by environment variable, because the spec left
// them unspecified and they are the difference between a useful system and one that is
// muted within a fortnight. The illness detector in particular has no stated
// thresholds anywhere in the spec, and it is the one most likely to fire spuriously.
//
// This project has already watched a notification channel become something to ignore.
// The lesson was not "send fewer" -- it was that a detector nobody can tune is a
// detector that eventually gets muted wholesale, taking the true positives with it.
public static class HealthThresholds
{
    private static double Env(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

    // A baseline below this many readings is arithmetic without meaning. Deliberately
    // higher for the sparser medium tier, which accumulates more slowly and drifts more.
    public static int MinBaselineN => EnvInt("VITARA_MIN_BASELINE_N", 21);

    public static int BaselineWindowDays => EnvInt("VITARA_BASELINE_WINDOW_DAYS", 60);

    // How far from personal normal counts as a deviation, in the user's own standard
    // deviations -- not population norms.
    public static double DeviationZ => Env("VITARA_DEVIATION_Z", 2.0);

    // Two days, so one odd night never speaks. The single most effective noise filter
    // available and the cheapest.
    public static int DeviationSustainedDays => EnvInt("VITARA_DEVIATION_DAYS", 2);

    // Illness signal. Each component is a z-score against that metric's own baseline;
    // two of three sustained triggers, all three raises confidence.
    public static double IllnessRestingHrZ => Env("VITARA_ILLNESS_RHR_Z", 1.5);
    public static double IllnessHrvZ => Env("VITARA_ILLNESS_HRV_Z", -1.5);
    public static double IllnessTempZ => Env("VITARA_ILLNESS_TEMP_Z", 1.5);
    public static int IllnessSustainedDays => EnvInt("VITARA_ILLNESS_DAYS", 2);

    // Skin temperature is the one metric here that arrives already baselined.
    //
    // The ring does not report a temperature; it reports a DEVIATION from its own
    // long-run reference for this finger. Vitara then baselines that deviation again
    // and scores it, which is a baseline of a baseline -- deliberate, because it
    // removes any standing offset (a sleeper who always runs +0.2 is not warm, that is
    // simply their normal) and because the ring's reference adapts on a different
    // schedule from ours. But it leaves two failure modes that a pure z-score cannot
    // see, and both of them are silent:
    //
    //   1. A tight distribution makes noise significant. Someone whose deviation
    //      barely moves has a small standard deviation, so a 0.05 C wobble -- inside
    //      the sensor's own resolution -- can clear 1.5 SD and read as a fever signal.
    //   2. A drifting reference absorbs a real rise. If the ring's own baseline is
    //      creeping up alongside the user, the reported deviation stays near zero
    //      while the person genuinely warms, and the z-score has nothing to see.
    //
    // So the z-score is bracketed by two absolutes, in degrees. Below the floor, an
    // unusual reading is not counted at all. At or above the override, a reading is
    // counted whatever its z-score says.
    public static double IllnessTempFloorC => Env("VITARA_ILLNESS_TEMP_FLOOR_C", 0.15);
    public static double IllnessTempOverrideC => Env("VITARA_ILLNESS_TEMP_OVERRIDE_C", 0.50);

    // Acute-to-chronic workload ratio, 7-day mean over 28-day mean.
    public static double AcwrLow => Env("VITARA_ACWR_LOW", 0.8);
    public static double AcwrHigh => Env("VITARA_ACWR_HIGH", 1.3);

    // Regime change. Both must be satisfied: a step big enough to matter, that then
    // stays put. Without the dwell requirement a twitchy detector resets the baseline
    // on every wobble, and once "normal" is redefined daily nothing is ever abnormal
    // again -- the detector quietly disables the entire system it feeds.
    public static double RegimeShiftZ => Env("VITARA_REGIME_SHIFT_Z", 1.5);
    public static int RegimeDwellDays => EnvInt("VITARA_REGIME_DWELL_DAYS", 14);

    // Drift: how many days without movement before a slow trend is worth saying.
    public static int DriftMinDays => EnvInt("VITARA_DRIFT_MIN_DAYS", 14);

    // How much accumulated deficit over a fortnight is worth raising. Five hours --
    // roughly twenty-five minutes a night. Below that it sits inside the error of the
    // need estimate the debt is measured against, and reporting it would mean
    // reporting that estimate's own uncertainty back as a finding.
    public static double SleepDebtThresholdMinutes => Env("VITARA_SLEEP_DEBT_MINUTES", 300);

    // Days without an expected reading before absence becomes a finding.
    public static int StalenessDays => EnvInt("VITARA_STALENESS_DAYS", 3);
}
