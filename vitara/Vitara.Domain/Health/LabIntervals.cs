namespace Vitara.Domain.Health;

// How long after a change a second blood draw can show it (Vitara ADR 0022).
//
// HbA1c reflects about three months of glucose; lipids settle in about six weeks;
// insulin resistance in about eight; CRP in about four. In the domain rather than the
// analysis layer because the form that starts an intervention has to say the date too.
public static class LabIntervals
{
    public static int MinimumDays(string metric) => metric switch
    {
        MetricKeys.Hba1c => 90,
        MetricKeys.TotalCholesterol or MetricKeys.Ldl or MetricKeys.Hdl or MetricKeys.Triglycerides
            or MetricKeys.NonHdl or MetricKeys.ApoB => 42,
        MetricKeys.FastingInsulin or MetricKeys.HomaIr => 56,
        MetricKeys.Crp or MetricKeys.HsCrp => 28,
        _ => 56,
    };
}
