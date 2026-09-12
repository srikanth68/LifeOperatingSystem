using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

public record MeasurementRequest(
    string Metric,
    double Value,
    string? Unit,
    DateTime? ObservedAtLocal,
    string? Note,

    // How it was taken. Optional, and the reading is still kept without it -- but a
    // blood pressure with no position is a reading that cannot join a baseline with
    // any others, so the form asks.
    bool? Fasting = null,
    string? TimeOfDay = null,
    string? Position = null,
    string? Arm = null);

// Readings a person entered by hand.
//
// In Vitara rather than Insight: this is data arriving, and ingestion and analysis were
// split so each owns its own tables. Insight projects these into observations the same
// way it projects Oura's.
//
// The medium tier had no home until now. Blood pressure, glucose and a hand-taken pulse
// were parseable by the health import and then discarded, and six of the ten algorithm
// categories in the health spec are blocked on having somewhere to put them.
[ApiController, Route("api/measurements")]
public class MeasurementsController(IVitaraRepository repo, ILogger<MeasurementsController> logger) : ControllerBase
{
    // Metrics the manual form offers, with the unit each is stored in.
    //
    // A closed list on purpose. Free text would let "bp", "BP" and "blood pressure"
    // become three metrics that never share a baseline, and a baseline split three ways
    // is three baselines that each stay invalid forever.
    private static readonly Dictionary<string, (string Unit, string Tier, string Label)> Allowed = new()
    {
        [MetricKeys.SystolicBp] = ("mmHg", Tiers.Medium, "Systolic BP"),
        [MetricKeys.DiastolicBp] = ("mmHg", Tiers.Medium, "Diastolic BP"),
        [MetricKeys.Pulse] = ("bpm", Tiers.Medium, "Pulse"),
        [MetricKeys.Glucose] = ("mg/dL", Tiers.Medium, "Glucose"),
        [MetricKeys.WeightKg] = ("kg", Tiers.Medium, "Weight"),
        [MetricKeys.WaistCircumferenceCm] = ("cm", Tiers.Medium, "Waist"),
        [MetricKeys.Vo2Max] = ("ml/kg/min", Tiers.Medium, "VO2 max"),
        [MetricKeys.Hba1c] = ("%", Tiers.Sparse, "HbA1c"),
        [MetricKeys.TotalCholesterol] = ("mg/dL", Tiers.Sparse, "Total cholesterol"),
        [MetricKeys.Ldl] = ("mg/dL", Tiers.Sparse, "LDL"),
        [MetricKeys.Hdl] = ("mg/dL", Tiers.Sparse, "HDL"),
        [MetricKeys.Triglycerides] = ("mg/dL", Tiers.Sparse, "Triglycerides"),
        [MetricKeys.Crp] = ("mg/L", Tiers.Sparse, "CRP"),
        [MetricKeys.Tsh] = ("mIU/L", Tiers.Sparse, "TSH"),
        [MetricKeys.VitaminD] = ("ng/mL", Tiers.Sparse, "Vitamin D"),
    };

    // What the form should offer, and which context questions matter per metric.
    // Served rather than hardcoded in the UI so the two can never disagree about
    // whether glucose asks "fasting?" -- BaselineKeys is the single source of that.
    [HttpGet("metrics")]
    public IActionResult Metrics() => Ok(Allowed.Select(kv => new
    {
        metric = kv.Key,
        kv.Value.Unit,
        kv.Value.Tier,
        kv.Value.Label,
        context = BaselineKeys.For(kv.Key),
    }));

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 90, [FromQuery] string? metric = null)
    {
        var to = LocalTime.Today;
        var from = to.AddDays(-Math.Clamp(days, 1, 3650));

        return Ok((await repo.GetMeasurementsAsync(from, to, metric)).Select(Describe));
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] MeasurementRequest req)
    {
        if (!Allowed.TryGetValue(req.Metric, out var spec))
            return BadRequest(new { error = $"Unknown metric \"{req.Metric}\".", allowed = Allowed.Keys });

        if (double.IsNaN(req.Value) || double.IsInfinity(req.Value))
            return BadRequest(new { error = "Value must be a number." });

        // A reading dated in the future is a typo, and it would sit at the end of every
        // window pulling trends toward a day that has not happened.
        var at = req.ObservedAtLocal ?? DateTime.Now;
        if (at > DateTime.Now.AddDays(1))
            return BadRequest(new { error = "That reading is dated in the future." });

        var context = new MeasurementContext(
            Fasting: req.Fasting,
            TimeOfDay: Blank(req.TimeOfDay),
            Position: Blank(req.Position),
            Arm: Blank(req.Arm));

        var measurement = new Measurement
        {
            Metric = req.Metric,
            Value = req.Value,
            Unit = string.IsNullOrWhiteSpace(req.Unit) ? spec.Unit : req.Unit,
            ObservedAtLocal = at,
            Day = DateOnly.FromDateTime(at),
            Tier = spec.Tier,
            Source = "manual",
            ContextJson = HasAny(context) ? JsonSerializer.Serialize(context) : null,
            Note = Blank(req.Note),
        };

        var written = await repo.UpsertMeasurementsAsync([measurement]);

        if (written == 0)
            return Ok(new { duplicate = true, message = "An identical reading is already recorded for that moment." });

        logger.LogInformation("Measurement recorded: {Metric} {Value}{Unit} at {At}.",
            measurement.Metric, measurement.Value, measurement.Unit, measurement.ObservedAtLocal);

        return Ok(Describe(measurement));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteMeasurementAsync(id) ? NoContent() : NotFound();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool HasAny(MeasurementContext c) =>
        c.Fasting is not null || c.TimeOfDay is not null || c.Position is not null || c.Arm is not null;

    private static object Describe(Measurement m) => new
    {
        m.Id,
        m.Metric,
        label = Allowed.TryGetValue(m.Metric, out var spec) ? spec.Label : m.Metric,
        m.Value,
        m.Unit,
        day = m.Day.ToString("yyyy-MM-dd"),
        at = m.ObservedAtLocal.ToString("yyyy-MM-dd HH:mm"),
        m.Tier,
        m.Source,
        m.Note,

        // The signature is shown, not hidden. It is the answer to "why is my evening
        // reading not being compared against my morning ones", and there is no other
        // way for the user to see that two readings live in different buckets.
        signature = BaselineKeys.Signature(m.Metric, Deserialise(m.ContextJson)),
    };

    private static MeasurementContext? Deserialise(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<MeasurementContext>(json); }
        catch (JsonException) { return null; }
    }
}
