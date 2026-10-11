using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

// The people, places, projects and things records are about, and the links between
// records. Links say what is related and on what basis (observed or inferred); NorthStar
// never decides on its own that two things are related.
[ApiController, Route("api/entities")]
public class EntitiesController(INorthStarRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Find([FromQuery] string? q, [FromQuery] string? type, [FromQuery] int limit = 50) =>
        Ok(await repo.FindEntitiesAsync(q, type?.ToLowerInvariant(), Math.Clamp(limit, 1, 500)));

    // Upsert by name or alias: an entity that is already known is returned (with any new
    // aliases merged), not duplicated.
    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] EntityRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("name is required");
        var (entity, created, error) = await repo.UpsertEntityAsync(req.Name, req.Type?.ToLowerInvariant(), req.Aliases, req.Note);
        return error is not null ? BadRequest(error) : Ok(new { entity, created });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var d = await repo.GetEntityDetailAsync(id);
        return d is null ? NotFound() : Ok(new { entity = d.Entity, links = d.Links });
    }

    // Aliases given here replace the list. Fields left out are unchanged.
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EntityRequest req)
    {
        var (entity, error) = await repo.UpdateEntityAsync(id, req.Name, req.Type?.ToLowerInvariant(), req.Aliases, req.Note);
        return error is null ? Ok(entity) : error == "not found" ? NotFound() : BadRequest(error);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteEntityAsync(id) ? NoContent() : NotFound();

    [HttpGet("types")]
    public IActionResult Types() => Ok(EntityTypes.All);
}

[ApiController, Route("api/links")]
public class LinksController(INorthStarRepository repo) : ControllerBase
{
    // Everything linked to one record, in both directions.
    [HttpGet]
    public async Task<IActionResult> For([FromQuery] string type, [FromQuery] string id)
    {
        if (!RecordTypes.IsValid(type)) return BadRequest($"type must be one of: {string.Join(", ", RecordTypes.All)}");
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("id is required");
        return Ok(await repo.GetLinksAsync(type, id));
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] LinkRequest req)
    {
        var (link, created, error) = await repo.AddLinkAsync(new RecordLink
        {
            FromType = req.FromType?.ToLowerInvariant() ?? "",
            FromId = req.FromId ?? "",
            Relation = req.Relation ?? "",
            ToType = req.ToType?.ToLowerInvariant() ?? "",
            ToId = req.ToId ?? "",
            Basis = req.Basis?.ToLowerInvariant() ?? LinkBasis.Observed,
            Source = string.IsNullOrWhiteSpace(req.Source) ? "agent" : req.Source.Trim(),
            SourceRef = string.IsNullOrWhiteSpace(req.SourceRef) ? null : req.SourceRef.Trim(),
        });
        return error is not null ? BadRequest(error) : Ok(new { link, created });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteLinkAsync(id) ? NoContent() : NotFound();
}

public record EntityRequest(string? Name, string? Type, string? Aliases, string? Note);

public record LinkRequest(
    string? FromType, string? FromId, string? Relation, string? ToType, string? ToId,
    string? Basis, string? Source, string? SourceRef);
