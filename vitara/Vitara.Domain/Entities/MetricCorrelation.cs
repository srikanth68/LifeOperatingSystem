namespace Vitara.Domain.Entities;

// A relationship between two metrics, as measured over one window.
//
// Stored rather than computed on request because it is expensive relative to how often
// it changes -- ninety days of data does not produce a different answer between one
// page load and the next -- and because keeping the history lets a relationship that
// strengthens or fades be seen doing it.
//
// CORRELATION, AND NOTHING MORE. The column names say Driver and Outcome because the
// pairs are lagged in one direction only, not because causation was established. Every
// surface that shows these has to say so; the data model cannot enforce it, but it can
// avoid pretending otherwise by never storing a word like "cause".
public class MetricCorrelation
{
    public long Id { get; set; }

    public string Driver { get; set; } = "";
    public string Outcome { get; set; } = "";

    // 0 = same day, 1 = the outcome measured the day after the driver.
    public int LagDays { get; set; }

    // Spearman's rho. Rank-based, so a single wrecked night does not drag it.
    public double Rho { get; set; }

    // Paired days the coefficient was computed from. Shown wherever rho is, because a
    // 0.45 over thirty days and a 0.45 over three hundred are different claims.
    public int N { get; set; }

    public double PValue { get; set; }

    public int WindowDays { get; set; }
    public DateOnly ComputedOnLocal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
