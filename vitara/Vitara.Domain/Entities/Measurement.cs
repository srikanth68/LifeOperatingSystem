namespace Vitara.Domain.Entities;

// A reading nothing automatic produced.
//
// Blood pressure, glucose, waist circumference, a pulse taken by hand -- the medium
// tier. Until now these had nowhere to live: the health import parsed them out of an
// Apple export and then reported them as "no store yet", and manual entry did not
// exist at all. Six of the ten algorithm categories in the health spec are blocked on
// exactly this table.
//
// ONE TABLE FOR ALL OF THEM, rather than a typed table per metric the way Oura's data
// is stored. The difference is that Oura's shape is fixed by Oura: a sleep session has
// the same fourteen fields every night. What a person measures by hand is open-ended,
// and adding a table plus a repository method plus a migration every time they start
// tracking something new is how manual entry quietly stops being extended.
//
// It is a TYPED table even so, not a write straight into Observations. Ingestion and
// analysis were separated on the basis that each owns its own tables -- vitara writes
// what arrives, insight writes what it derives -- and a manual reading is something
// that arrives. The projector picks it up from here like everything else.
public class Measurement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Canonical key from MetricKeys where one exists, free text otherwise. A lab
    // analyte nobody enumerated in advance is still worth keeping, and can be promoted
    // to a constant later without touching the stored rows.
    public string Metric { get; set; } = "";

    public double Value { get; set; }
    public string Unit { get; set; } = "";

    // LOCAL, like every other day boundary in this system. A reading taken at 11pm
    // belongs to that evening, not to the next UTC day.
    public DateTime ObservedAtLocal { get; set; }
    public DateOnly Day { get; set; }

    public string Tier { get; set; } = "medium";    // medium | sparse
    public string Source { get; set; } = "manual";  // manual | apple_health | lab

    // How it was taken, serialised MeasurementContext.
    //
    // This is the whole reason a blood pressure reading is worth more than a number.
    // Seated-morning and standing-evening are different quantities that share a name,
    // and BaselineKeys splits the baseline on exactly the fields that matter while
    // keeping the rest for explaining an outlier six months later.
    public string? ContextJson { get; set; }

    // What the user typed. Never parsed, never acted on -- "after the gym", "felt
    // dizzy" is context no schema will ever hold, and it is the part they will want
    // when they look back.
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
