namespace Vitara.Domain.Entities;

// The tables that record WHY a number moved.
//
// These have to exist before anything consumes them, and that is not premature
// building — it is the only time they can be created. Measurement context, a device
// swap, the week you had flu: none of it can be reconstructed a year later from the
// readings themselves. The information is gone at entry time or it is gone for good.

// A ring, a cuff, a scale.
//
// A ring generation change or a firmware update shifts absolute values. Without this,
// the system reads a hardware swap as a health event -- and worse, a rolling baseline
// quietly absorbs the shift over the following weeks and calls the new numbers normal,
// so the change is never flagged and never explained.
public class Device
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";           // ring | bp_cuff | scale | phone
    public string Model { get; set; } = "";
    public string? Firmware { get; set; }
    public DateOnly ActiveFromLocal { get; set; }
    public DateOnly? ActiveToLocal { get; set; }
    public string? Notes { get; set; }
}

// Something started or changed on purpose.
//
// If a medication starts and resting heart rate steps down, a 60-day rolling baseline
// absorbs it within two months and reports the new value as normal. The shift itself --
// the interesting part -- is never surfaced. Interventions are the record that lets a
// detected step change be attributed rather than merely noticed.
public class Intervention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "supplement"; // medication | supplement | protocol | dose_change
    public string Name { get; set; } = "";
    public string? Dose { get; set; }
    public DateOnly StartedOnLocal { get; set; }
    public DateOnly? EndedOnLocal { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A stretch that should not shape what counts as normal.
//
// Two weeks of illness eaten by a rolling window raises the baseline, and the system
// then stops flagging exactly the thing it exists to flag. Excluded periods drop out of
// baseline computation while staying fully visible in history and on charts -- the data
// is real, it just should not define normal.
public class ExcludedPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly StartLocal { get; set; }
    public DateOnly EndLocal { get; set; }
    public string Reason { get; set; } = "other";    // illness | injury | travel | training_block | device_change | other
    public bool ExcludeFromBaseline { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Time spent in another timezone.
//
// Day boundaries are local, but "local" moves when the user flies. Cross-timezone
// travel corrupts sleep timing and day bucketing in ways that look exactly like a
// circadian problem. Sleep and circadian metrics during travel are tagged and, by
// default, kept out of baselines.
public class TravelPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly StartLocal { get; set; }
    public DateOnly EndLocal { get; set; }
    public string HomeTz { get; set; } = "America/New_York";
    public string AwayTz { get; set; } = "";
    public string? Notes { get; set; }
}

// A blood draw, grouping the analytes taken together.
public class LabPanel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly DrawnOnLocal { get; set; }
    public string? LabName { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// What a lab considers normal for an analyte.
//
// Lab-specific where known, because reporting conventions genuinely differ between
// labs and a value flagged against the wrong range is worse than one not flagged at
// all. Seeded with common ranges and editable.
public class ReferenceRange
{
    public int Id { get; set; }
    public string Metric { get; set; } = "";
    public double? Low { get; set; }
    public double? High { get; set; }
    public string Unit { get; set; } = "";
    public string? Sex { get; set; }                 // null = applies to all
    public int? AgeMin { get; set; }
    public int? AgeMax { get; set; }
    public string? LabName { get; set; }             // null = generic
    public string? Notes { get; set; }
}

// What normal looks like for one metric, in one context, over one window.
//
// Robust statistics alongside the mean and standard deviation on purpose. The spec's
// approach -- compute the spread, then discard anything more than three deviations
// from the spread you just computed -- is circular, and unstable at n around 60 on
// data that is rarely symmetric. Median and the quartiles do not have that problem,
// and are what the outlier rule actually uses.
public class Baseline
{
    public long Id { get; set; }
    public string Metric { get; set; } = "";
    public string BaselineSignature { get; set; } = "";
    public DateOnly ComputedOnLocal { get; set; }
    public int WindowDays { get; set; }

    public double Mean { get; set; }
    public double StdDev { get; set; }
    public double Median { get; set; }
    public double P25 { get; set; }
    public double P75 { get; set; }
    public int N { get; set; }

    // Below the minimum sample size the numbers above are arithmetic without meaning.
    // Exposed as a flag rather than suppressed, so a caller can say "not enough data
    // yet" instead of silently showing nothing or, worse, showing garbage.
    public bool IsValid { get; set; }

    // Which periods were dropped, so a baseline can explain itself when someone asks
    // why a number looks different from what they expected.
    public string? ExclusionsJson { get; set; }

    // The regime this baseline belongs to. A detected step change closes one baseline
    // and opens the next, rather than letting the old level contaminate the new one.
    public DateOnly? RegimeStartLocal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A computed value that is not a raw measurement -- a z-score, a ratio, a slope.
public class DerivedMetric
{
    public long Id { get; set; }
    public string Metric { get; set; } = "";
    public DateOnly ObservedDateLocal { get; set; }
    public double Value { get; set; }

    // What fed it. Auditability is the point: a derived number nobody can trace back
    // to its inputs is indistinguishable from one the system made up.
    public string? InputsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
