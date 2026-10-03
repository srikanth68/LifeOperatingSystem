using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

// Blood work: the sparse tier, and the one place this system uses a population number.
//
// Everything else in Vitara is compared against the person's own history, because a
// ring produces a reading a day and ninety of them make a personal normal. Labs arrive
// twice a year. Four points over two years is a sequence to annotate, not a
// distribution to learn from, so a lab value is read against two things instead: the
// reference range a laboratory prints, and the last draw.
//
// A panel is entered as a unit. Twelve analytes off one needle are one event -- they
// share a date, a lab and a fasting state, and typing that twelve times is how a
// feature stops being used by March.
[ApiController, Route("api/labs")]
public class LabsController(IVitaraRepository repo) : ControllerBase
{
    public record LabResultInput(string Metric, double Value, string? Unit, string? Note);

    public record LabPanelInput(
        string? DrawnOn,                 // yyyy-MM-dd; today when omitted
        string? LabName,
        string? Notes,
        List<LabResultInput> Results,
        bool? Fasting = null);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int limit = 20)
    {
        var panels = await repo.GetLabPanelsAsync(limit);
        if (panels.Count == 0) return Ok(Array.Empty<object>());

        var oldest = panels.Min(p => p.DrawnOnLocal);
        var all = await repo.GetMeasurementsAsync(oldest, LocalTime.Today);
        var ranges = await repo.GetReferenceRangesAsync();
        var profile = await repo.GetProfileAsync();

        // Draw-to-draw, per analyte. The comparison a person actually wants is "since
        // last time", and last time means the previous PANEL -- not the previous row,
        // which for someone who also measures glucose at home would be this morning.
        var byPanel = all.Where(m => m.LabPanelId is not null)
            .GroupBy(m => m.LabPanelId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var ordered = panels.OrderBy(p => p.DrawnOnLocal).ToList();

        return Ok(ordered.AsEnumerable().Reverse().Select(panel =>
        {
            var results = byPanel.TryGetValue(panel.Id, out var rows) ? rows : [];
            var index = ordered.FindIndex(p => p.Id == panel.Id);

            // Several draws of the same analyte on one day should not silently pick
            // one; the last entered wins, which is what a correction looks like.
            var values = results
                .GroupBy(m => m.Metric)
                .ToDictionary(g => g.Key, g => g.OrderBy(m => m.CreatedAt).Last().Value);

            var derived = DerivedLabs.From(values, panel.Fasting, profile?.BiologicalSex, profile?.Age);

            (string Word, string Text, object? Band) Standing(string metric, double value)
            {
                var range = ReferenceRanges.For(ranges, metric, profile?.BiologicalSex, profile?.Age, panel.LabName);
                var where = ReferenceRanges.Where(value, range);

                return (where.ToString().ToLowerInvariant(),
                        ReferenceRanges.Describe(where, range),
                        range is null ? null : new { range.Low, range.High, band = ReferenceRanges.Band(range), range.Notes });
            }

            // The nearest earlier panel that measured the same analyte -- not simply the
            // panel before this one, since panels differ in what they contain.
            double? Previous(string metric)
            {
                for (var i = index - 1; i >= 0; i--)
                    if (byPanel.TryGetValue(ordered[i].Id, out var earlier))
                        if (earlier.FirstOrDefault(m => m.Metric == metric) is { } hit)
                            return hit.Value;

                return null;
            }

            return new
            {
                id = panel.Id,
                drawnOn = panel.DrawnOnLocal.ToString("yyyy-MM-dd"),
                daysAgo = LocalTime.Today.DayNumber - panel.DrawnOnLocal.DayNumber,
                panel.LabName,
                panel.Notes,
                panel.Fasting,

                // Worked out from the draw rather than measured in it: non-HDL, HOMA-IR
                // and eGFR, each of which the report usually leaves to the reader.
                derived = derived.Values.Select(v => new
                {
                    metric = v.Metric,
                    label = MetricCatalogue.Find(v.Metric)?.Label ?? v.Metric,
                    v.Value,
                    v.Unit,
                    v.Method,
                    v.From,
                    v.Caveat,
                    grade = v.Grade.ToString(),
                    gradeLabel = Evidence.Label(v.Grade),

                    standing = Standing(v.Metric, v.Value).Word,
                    standingText = Standing(v.Metric, v.Value).Text,
                    range = Standing(v.Metric, v.Value).Band,
                }),

                // And what could not be worked out, with the reason. A panel missing
                // fasting insulin is structurally blind to insulin resistance, and
                // saying so is worth more than any value would have been.
                gaps = derived.Refusals.Select(r => new
                {
                    metric = r.Metric,
                    r.Label,
                    r.Reason,
                    missing = r.Missing.Select(m => new
                    {
                        key = m,
                        label = MetricCatalogue.Find(m)?.Label ?? m,
                    }),
                }),
                results = results.Select(m =>
                {
                    var info = MetricCatalogue.Find(m.Metric);
                    var range = ReferenceRanges.For(ranges, m.Metric, profile?.BiologicalSex, profile?.Age, panel.LabName);
                    var standing = ReferenceRanges.Where(m.Value, range);
                    var previous = Previous(m.Metric);

                    return new
                    {
                        metric = m.Metric,
                        label = info?.Label ?? m.Metric,
                        m.Value,
                        unit = string.IsNullOrWhiteSpace(m.Unit) ? info?.Unit ?? "" : m.Unit,
                        m.Note,

                        // Said as a word and a sentence, never as a colour alone. These
                        // numbers get read on a phone at a GP's desk.
                        standing = standing.ToString().ToLowerInvariant(),
                        standingText = ReferenceRanges.Describe(standing, range),
                        range = range is null ? null : new
                        {
                            range.Low, range.High, range.Unit, range.Sex, range.LabName, range.Notes,
                            band = ReferenceRanges.Band(range),
                        },

                        previous,
                        change = previous is { } p ? Math.Round(m.Value - p, 3) : (double?)null,
                    };
                }).OrderBy(r => r.label, StringComparer.Ordinal),
            };
        }));
    }

    // Every analyte this understands, so a form can offer them rather than asking
    // somebody to remember that the key is "vitamin_d".
    [HttpGet("analytes")]
    public async Task<IActionResult> Analytes()
    {
        var ranges = await repo.GetReferenceRangesAsync();
        var profile = await repo.GetProfileAsync();

        return Ok(MetricCatalogue.All
            .Where(m => m.Group is "Labs" or "Vitals")
            .Select(m =>
            {
                var range = ReferenceRanges.For(ranges, m.Key, profile?.BiologicalSex, profile?.Age);
                return new
                {
                    m.Key,
                    m.Label,
                    m.Unit,
                    m.Group,
                    m.What,
                    m.Decimals,
                    range = range is null ? null : new { range.Low, range.High, band = ReferenceRanges.Band(range), range.Notes },

                    // How well established this measure is, carried with it rather
                    // than left to the wording of a label.
                    grade = Evidence.GradeFor(m.Key)?.ToString(),
                    gradeLabel = Evidence.For(m.Key) is { } e ? Evidence.Label(e.Grade) : null,
                    caveat = Evidence.For(m.Key)?.Caveat,
                };
            }));
    }

    [HttpGet("ranges")]
    public async Task<IActionResult> Ranges() => Ok((await repo.GetReferenceRangesAsync())
        .Select(r => new
        {
            r.Id, r.Metric,
            label = MetricCatalogue.Find(r.Metric)?.Label ?? r.Metric,
            r.Low, r.High, r.Unit, r.Sex, r.LabName, r.Notes,
            band = ReferenceRanges.Band(r),
        }));

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] LabPanelInput input)
    {
        if (input.Results is null || input.Results.Count == 0)
            return BadRequest(new { error = "A panel needs at least one result." });

        var drawn = DateOnly.TryParse(input.DrawnOn, out var parsed) ? parsed : LocalTime.Today;

        // A draw dated in the future is a typo, and storing it would put a reading
        // beyond every window that looks for it -- invisible rather than wrong, which
        // is worse.
        if (drawn > LocalTime.Today.AddDays(1))
            return BadRequest(new { error = $"That draw is dated {drawn:yyyy-MM-dd}, which is in the future." });

        var panel = new LabPanel
        {
            DrawnOnLocal = drawn,
            LabName = string.IsNullOrWhiteSpace(input.LabName) ? null : input.LabName.Trim(),
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            Fasting = input.Fasting,
        };

        // Carried onto each reading as well as onto the draw. Glucose baselines split
        // on fasting state, and a fasting lab glucose pooled with this morning's
        // post-breakfast reading corrupts the bucket it was meant to improve.
        var context = input.Fasting is null
            ? null
            : JsonSerializer.Serialize(new MeasurementContext(Fasting: input.Fasting));

        var at = drawn.ToDateTime(new TimeOnly(8, 0));   // draws are morning things
        var results = new List<Measurement>();
        var unknown = new List<string>();

        foreach (var r in input.Results)
        {
            if (string.IsNullOrWhiteSpace(r.Metric)) continue;

            var key = r.Metric.Trim();
            var info = MetricCatalogue.Find(key);

            // An analyte nobody enumerated is still kept, and is reported back as
            // unrecognised rather than dropped. Someone's panel will always contain one
            // more thing than this catalogue does, and losing it silently would teach
            // them not to bother entering any of it.
            if (info is null) unknown.Add(key);

            results.Add(new Measurement
            {
                Metric = key,
                Value = r.Value,
                Unit = string.IsNullOrWhiteSpace(r.Unit) ? info?.Unit ?? "" : r.Unit.Trim(),
                ObservedAtLocal = at,
                ContextJson = context,
                Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim(),
            });
        }

        if (results.Count == 0) return BadRequest(new { error = "None of those results had a metric." });

        var saved = await repo.SaveLabPanelAsync(panel, results);

        return Ok(new
        {
            id = saved.Id,
            drawnOn = saved.DrawnOnLocal.ToString("yyyy-MM-dd"),
            stored = results.Count,
            unrecognised = unknown,
            note = unknown.Count == 0
                ? null
                : "Stored, but these are not analytes this system knows about, so they will not be " +
                  "compared against a reference range or carried into findings: " + string.Join(", ", unknown),
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteLabPanelAsync(id) ? NoContent() : NotFound();
}
