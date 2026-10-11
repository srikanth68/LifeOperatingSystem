using Microsoft.AspNetCore.Mvc;
using NorthStar.Application.Interfaces;
using NorthStar.Domain.Entities;

namespace NorthStar.API.Controllers;

// Agent memory API — the store any agent harness (San's agent loop, openclaw, custom) persists to.
//
// Who may write what: anything an agent saves starts as PROPOSED unless it says the
// user stated it directly (status=active). The owner writing through the UI is active
// from the start. Proposed memories are recalled, labelled, and wait in /review for the
// owner to confirm or reject -- an agent's conclusion never becomes settled fact on its
// own say-so.
[ApiController, Route("api/memory")]
public class MemoryController(INorthStarRepository repo) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] MemoryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest("content is required");
        var kind = (req.Kind ?? "observation").ToLowerInvariant();
        if (!MemoryKinds.IsValid(kind)) return BadRequest($"kind must be one of: {string.Join(", ", MemoryKinds.All)}");
        var status = req.Status?.ToLowerInvariant();
        if (status is not null && !MemoryStatus.IsLive(status))
            return BadRequest("status must be active or proposed");

        var source = string.IsNullOrWhiteSpace(req.Source) ? "agent" : req.Source.Trim();
        var result = await repo.RememberAsync(new MemoryEntry
        {
            Content = req.Content.Trim(),
            Kind = kind,
            Source = source,
            Tags = req.Tags ?? "",
            Importance = Math.Clamp(req.Importance ?? 3, 1, 5),
            Status = status ?? InitialStatus(source),
            Supersedes = req.Supersedes,
            SourceRef = Trimmed(req.SourceRef, 300),
            Evidence = Trimmed(req.Evidence, 1000),
        }, req.Entities);

        if (result.Error is not null)
            return result.Error.EndsWith("not found") ? NotFound(result.Error) : Conflict(result.Error);

        var body = ToResult(result.Memory!);
        body["duplicate"] = result.Duplicate;
        body["note"] = result.Note;
        body["similar"] = result.Similar.Select(m => new { m.Id, m.Content, m.Kind, m.Status, m.CreatedAt });
        return Ok(body);
    }

    [HttpGet("recall")]
    public async Task<IActionResult> Recall([FromQuery] string q, [FromQuery] string? kind, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest("query parameter 'q' is required");
        var results = await repo.RecallMemoriesAsync(q, kind?.ToLowerInvariant(), Math.Clamp(limit, 1, 50));
        return Ok(new { query = q, count = results.Count, memories = results.Select(ToResult) });
    }

    // status: omitted = live (active + proposed), "all", or one of MemoryStatus.All.
    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int limit = 20, [FromQuery] string? kind = null, [FromQuery] string? status = null)
    {
        status = status?.ToLowerInvariant();
        if (status is not null && status != "all" && !MemoryStatus.All.Contains(status))
            return BadRequest($"status must be all or one of: {string.Join(", ", MemoryStatus.All)}");
        var results = await repo.GetRecentMemoriesAsync(Math.Clamp(limit, 1, 500), kind?.ToLowerInvariant(), status);
        return Ok(results.Select(ToResult));
    }

    // The owner's queue: everything proposed, newest first, with what each would replace.
    [HttpGet("review")]
    public async Task<IActionResult> Review([FromQuery] int limit = 100)
    {
        var proposed = await repo.GetRecentMemoriesAsync(Math.Clamp(limit, 1, 500), null, MemoryStatus.Proposed);
        var items = new List<Dictionary<string, object?>>();
        foreach (var m in proposed)
        {
            var item = ToResult(m);
            item["replaces"] = m.Supersedes is { } t ? await repo.DescribeAsync(RecordTypes.Memory, t.ToString()) : null;
            items.Add(item);
        }
        return Ok(new { count = items.Count, memories = items });
    }

    // Provenance and history: where it came from, what it replaced, what replaced it.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var d = await repo.GetMemoryDetailAsync(id);
        if (d is null) return NotFound();
        return Ok(new
        {
            memory = ToResult(d.Memory),
            history = d.History.Select(ToResult),
            entities = d.Entities.Select(e => new { e.Id, e.Name, e.Type }),
            links = await repo.GetLinksAsync(RecordTypes.Memory, id.ToString()),
        });
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] StatusChangeRequest? req)
    {
        var (m, error) = await repo.ConfirmMemoryAsync(id, req?.Note);
        return error is null ? Ok(ToResult(m!)) : error == "not found" ? NotFound() : Conflict(error);
    }

    [HttpPost("{id:guid}/retract")]
    public async Task<IActionResult> Retract(Guid id, [FromBody] StatusChangeRequest? req)
    {
        var (m, error) = await repo.RetractMemoryAsync(id, req?.Note);
        return error is null ? Ok(ToResult(m!)) : error == "not found" ? NotFound() : Conflict(error);
    }

    // Permanent removal, for what should never have been stored at all. A memory that
    // was merely wrong is retracted instead, so the record of the mistake survives.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeleteMemoryAsync(id) ? NoContent() : NotFound();

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var (total, byKind, byStatus) = await repo.GetMemoryStatsAsync();
        return Ok(new { total, byKind, byStatus });
    }

    // The owner, through the UI, is the one source whose word needs no confirming.
    internal static string InitialStatus(string source) =>
        source.Equals("manual", StringComparison.OrdinalIgnoreCase) ? MemoryStatus.Active : MemoryStatus.Proposed;

    private static string? Trimmed(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];

    private static Dictionary<string, object?> ToResult(MemoryEntry m) => new()
    {
        ["id"] = m.Id, ["content"] = m.Content, ["kind"] = m.Kind, ["source"] = m.Source,
        ["tags"] = m.Tags, ["importance"] = m.Importance, ["createdAt"] = m.CreatedAt,
        ["lastAccessedAt"] = m.LastAccessedAt, ["accessCount"] = m.AccessCount,
        ["status"] = m.Status, ["statusChangedAt"] = m.StatusChangedAt, ["statusNote"] = m.StatusNote,
        ["supersedes"] = m.Supersedes, ["supersededBy"] = m.SupersededBy,
        ["sourceRef"] = m.SourceRef, ["evidence"] = m.Evidence,
    };
}

// Status: "active" when the user stated it directly or the owner wrote it; omitted, an
// agent's memory is proposed. Supersedes: the id of the memory this one corrects.
// Entities: names it is about ("Honda Civic", or typed: "vehicle:Honda Civic").
public record MemoryRequest(
    string Content, string? Kind, string? Tags, int? Importance, string? Source,
    string? Status = null, Guid? Supersedes = null, string? SourceRef = null, string? Evidence = null,
    List<string>? Entities = null);

public record StatusChangeRequest(string? Note);
