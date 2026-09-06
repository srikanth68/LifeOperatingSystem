namespace Vitara.Domain.Entities;

// One measured value, whatever measured it.
//
// ADDITIVE, NOT A REPLACEMENT. Vitara's fifteen typed tables -- SleepSession,
// DailyReadiness, DailyActivity and the rest -- stay exactly as they are and remain
// the source of truth for ingest and for everything the dashboard and San already
// read. Observations are a second, flat view of the same numbers, written alongside
// them, and only the analytics layer reads from here.
//
// The alternative was to collapse those fifteen tables into this one. That is the
// tidier design and the wrong trade: it rewrites every existing query, breaks the
// dashboard and San's context on the way through, and turns a couple of weeks of work
// into a couple of months. Nothing that currently works has to change for baselines
// to exist.
//
// Heart-rate samples are deliberately NOT projected here. Oura records heart rate
// continuously -- thousands of rows a day -- and it is the one table that would fill
// the disk on a box that is also hosting the model. It stays in HeartRate, pruned at
// 90 days, and the analytics layer reads daily aggregates instead.
public class Observation
{
    public long Id { get; set; }

    // Canonical key from MetricKeys where known. Free text otherwise: a lab analyte
    // nobody enumerated in advance is still worth keeping.
    public string Metric { get; set; } = "";

    public double Value { get; set; }
    public string Unit { get; set; } = "";

    // LOCAL time, always. A day boundary means the user's day in their own timezone --
    // sleep that starts at 11pm belongs to that night, not to the next UTC day.
    public DateTime ObservedAtLocal { get; set; }
    public DateOnly ObservedDateLocal { get; set; }

    public string Tier { get; set; } = "dense";      // dense | medium | sparse
    public string Source { get; set; } = "oura";     // oura | manual | lab | apple_health

    // The identifier the source itself used, where it has one. Makes re-ingest
    // idempotent without depending on timestamps matching to the millisecond.
    public string? SourceRecordId { get; set; }

    public int? DeviceId { get; set; }
    public Guid? LabPanelId { get; set; }

    // How it was taken, serialised. Stored on every reading regardless of whether the
    // metric baselines on it -- the fields that do not split a baseline are still what
    // explains an outlier six months later.
    public string? ContextJson { get; set; }

    // Which baseline this reading belongs to, precomputed at write time from the
    // metric's declared context fields. Empty string means the metric does not split
    // on context, which is the common case for anything a device measured.
    public string BaselineSignature { get; set; } = "";

    // False when the metric needs context to be interpretable and the context is
    // missing. The row is kept and displayed; it just does not feed a baseline, because
    // a glucose reading of unknown fasting state belongs to neither bucket and would
    // quietly corrupt whichever one it landed in.
    public bool EligibleForBaseline { get; set; } = true;

    // What arrived, before unit normalisation. Labs report the same analyte in mg/dL or
    // mmol/L depending on the lab, and the original is the only way to check a
    // conversion later or to correct one that was wrong.
    public double? ValueOriginal { get; set; }
    public string? UnitOriginal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
