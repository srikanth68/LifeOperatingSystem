using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

// What you deliberately changed, and what you expected it to do.
//
// Interventions already existed as context -- something to check against when a metric
// stepped to a new level, so an explained change is not reported as a mystery. Nothing
// could create one, and nothing asked what it was for.
//
// The addition is a single field, and it is the one that makes evaluation possible at
// all: the metric this is supposed to move, named now rather than later. Recording the
// target afterwards is choosing the answer after seeing it, and over thirty metrics
// something always improved.
[ApiController, Route("api/interventions")]
public class InterventionsController(IVitaraRepository repo) : ControllerBase
{
    public record Input(
        string Name,
        string? Kind,
        string? Dose,
        string? StartedOn,
        string? TargetMetric,
        string? Notes);

    private static readonly string[] Kinds = ["medication", "supplement", "protocol", "dose_change"];

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var rows = await repo.GetInterventionsAsync();
        var today = LocalTime.Today;

        return Ok(rows.OrderByDescending(i => i.StartedOnLocal).Select(i => new
        {
            i.Id,
            i.Name,
            i.Kind,
            i.Dose,
            startedOn = i.StartedOnLocal.ToString("yyyy-MM-dd"),
            endedOn = i.EndedOnLocal?.ToString("yyyy-MM-dd"),
            daysIn = (i.EndedOnLocal ?? today).DayNumber - i.StartedOnLocal.DayNumber,
            running = i.EndedOnLocal is null,
            i.TargetMetric,
            targetLabel = i.TargetMetric is { } m ? MetricCatalogue.Find(m)?.Label ?? m : null,
            i.Notes,
        }));
    }

    // What can be chosen as a target: anything with a direction that is better and a
    // direction that is worse. A metric read against a range rather than as something
    // to move -- TSH, ferritin -- cannot answer "did this improve it", so it is not
    // offered rather than offered and then refused.
    [HttpGet("targets")]
    public IActionResult Targets() => Ok(MetricCatalogue.All
        .Where(m => MetricDirection.Polarity(m.Key) != MetricDirection.Neutral)
        .Select(m => new
        {
            m.Key,
            m.Label,
            m.Unit,
            m.Group,
            m.Tier,
            m.What,
            better = MetricDirection.Polarity(m.Key) == MetricDirection.HigherIsBetter ? "higher" : "lower",
        }));

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Input input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { error = "Give it a name you will recognise in three months." });

        var kind = string.IsNullOrWhiteSpace(input.Kind) ? "protocol" : input.Kind.Trim().ToLowerInvariant();
        if (!Kinds.Contains(kind))
            return BadRequest(new { error = $"Kind must be one of: {string.Join(", ", Kinds)}." });

        var started = DateOnly.TryParse(input.StartedOn, out var parsed) ? parsed : LocalTime.Today;

        if (started > LocalTime.Today.AddDays(1))
            return BadRequest(new { error = $"That start date is {started:yyyy-MM-dd}, which is in the future." });

        var target = string.IsNullOrWhiteSpace(input.TargetMetric) ? null : input.TargetMetric.Trim();

        if (target is not null)
        {
            if (MetricCatalogue.Find(target) is null)
                return BadRequest(new { error = $"\"{target}\" is not a metric this system knows about." });

            // Refused rather than accepted and then found unevaluable later. Being
            // told now, while the target can still be changed, is the useful moment.
            if (MetricDirection.Polarity(target) == MetricDirection.Neutral)
            {
                var label = MetricCatalogue.Find(target)!.Label;
                return BadRequest(new
                {
                    error = $"{label} has no better or worse direction — it is read against a range rather " +
                            "than as something to move, so there would be no way to say whether this helped. " +
                            "Pick something with a direction, or leave the target blank and record this as " +
                            "context only.",
                });
            }
        }

        var saved = await repo.SaveInterventionAsync(new Intervention
        {
            Name = input.Name.Trim(),
            Kind = kind,
            Dose = string.IsNullOrWhiteSpace(input.Dose) ? null : input.Dose.Trim(),
            StartedOnLocal = started,
            TargetMetric = target,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
        });

        return Ok(new
        {
            id = saved.Id,
            startedOn = saved.StartedOnLocal.ToString("yyyy-MM-dd"),

            // Said at the moment of starting, because it is the moment somebody is
            // most inclined to check tomorrow and conclude something.
            verdictFrom = target is null
                ? null
                : saved.StartedOnLocal.AddDays(InterventionEvalWindow.RunInDays + InterventionEvalWindow.WindowDays)
                    .ToString("yyyy-MM-dd"),
            note = target is null
                ? "Recorded as context. Without a target metric it will explain a step change in your data, " +
                  "but it will not get a verdict — choosing what it was meant to do after seeing what moved " +
                  "is not evidence."
                : $"The first {InterventionEvalWindow.RunInDays} days are not counted, and " +
                  $"{InterventionEvalWindow.WindowDays} are needed after that. Nothing conclusive before then.",
        });
    }

    // Stopped, not deleted. A protocol that ran three months and ended is part of the
    // record of why a metric moved; removing it takes the explanation with it.
    [HttpPost("{id:guid}/stop")]
    public async Task<IActionResult> Stop(Guid id, [FromQuery] string? on)
    {
        var ended = DateOnly.TryParse(on, out var parsed) ? parsed : LocalTime.Today;
        return await repo.EndInterventionAsync(id, ended)
            ? Ok(new { id, endedOn = ended.ToString("yyyy-MM-dd") })
            : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteInterventionAsync(id) ? NoContent() : NotFound();
}

// The two windows, duplicated here rather than referenced.
//
// Vitara.API does not reference Vitara.Insight -- ingestion and analysis are separate
// processes that share a database and nothing else, which is the whole reason the
// split exists. These two numbers are needed at the moment of CREATING an
// intervention, to say when a verdict becomes possible, and a project reference across
// that boundary to fetch two integers would undo the separation for no benefit.
//
// Pinned equal by a test, so they cannot drift apart silently.
public static class InterventionEvalWindow
{
    public const int RunInDays = 14;
    public const int WindowDays = 28;
}
