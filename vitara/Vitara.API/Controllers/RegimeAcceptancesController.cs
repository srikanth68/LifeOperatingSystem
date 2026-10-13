using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

// "This is my normal now."
//
// A step in the harmful direction that nothing recorded explains is held: the baseline
// stays at the level before it, and the finding stays open. Recording a reason -- the
// medication, the new ring, the illness -- releases it on the next pass. Sometimes there
// is no reason to record and the person simply knows: a new job with more stairs, a
// deliberate change of routine. This is that statement.
//
// Written here, not by vitara-insight, because it is user input. The analysis process
// reads it and releases the hold; the two processes never write the same table.
[ApiController, Route("api/regime-acceptances")]
public class RegimeAcceptancesController(IVitaraRepository repo) : ControllerBase
{
    public record Input(string Metric, string? Signature, string ChangePoint, string? Note);

    [HttpGet]
    public async Task<IActionResult> Get() =>
        Ok((await repo.GetRegimeAcceptancesAsync()).Select(a => new
        {
            a.Metric,
            signature = a.BaselineSignature,
            changePoint = a.ChangePointLocal.ToString("yyyy-MM-dd"),
            acceptedOn = a.AcceptedOnLocal.ToString("yyyy-MM-dd"),
            a.Note,
        }));

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Input input)
    {
        if (!DateOnly.TryParse(input.ChangePoint, out var changePoint))
            return BadRequest(new { error = "changePoint must be a date (yyyy-MM-dd)." });

        var signature = input.Signature ?? "";

        // Only a step that is actually held can be accepted. Accepting a date nothing was
        // held at would be a row that silently does nothing.
        var hold = (await repo.GetRegimeHoldsAsync()).FirstOrDefault(h =>
            h.Metric == input.Metric && h.BaselineSignature == signature && h.ChangePointLocal == changePoint);

        if (hold is null)
            return NotFound(new { error = "No held step for that metric and date." });
        if (!hold.IsOpen)
            return Conflict(new { error = $"That step is no longer held: it was {hold.ReleaseReason} on {hold.ReleasedLocal:yyyy-MM-dd}." });

        var saved = await repo.SaveRegimeAcceptanceAsync(new RegimeAcceptance
        {
            Metric = hold.Metric,
            BaselineSignature = hold.BaselineSignature,
            ChangePointLocal = hold.ChangePointLocal,
            AcceptedOnLocal = LocalTime.Today,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
        });

        var label = MetricCatalogue.Find(hold.Metric)?.Label ?? hold.Metric;
        return Ok(new
        {
            acceptedOn = saved.AcceptedOnLocal.ToString("yyyy-MM-dd"),
            note = $"Your normal for {label} will be rebuilt from {hold.ChangePointLocal:d MMM} on the next " +
                   "analysis pass, usually within the hour.",
        });
    }
}
