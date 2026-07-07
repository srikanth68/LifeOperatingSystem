using Microsoft.AspNetCore.Mvc;
using Sutra.Application.DTOs;
using Sutra.Application.Interfaces;
using Sutra.Domain.Entities;

namespace Sutra.API.Controllers;

[ApiController, Route("api/documents")]
public class DocumentsController(ISutraRepository repo, IDocumentStorage storage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] string? source, [FromQuery] string? q)
    {
        var docs = await repo.ListAsync(category, source, q);
        return Ok(docs.Select(ToResult));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var doc = await repo.GetAsync(id);
        return doc is null ? NotFound() : Ok(ToResult(doc));
    }

    [HttpPost]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFileCollection files,
        [FromForm] string? category,
        [FromForm] string? tags,
        [FromForm] string? sourceModule,
        [FromForm] string? sourceRefId,
        [FromForm] string? expiresAt,
        [FromForm] string? notes)
    {
        if (files.Count == 0) return BadRequest("No files provided.");

        var cat = string.IsNullOrWhiteSpace(category) ? "other" : category.ToLower();
        Guid? refId = Guid.TryParse(sourceRefId, out var parsed) ? parsed : null;
        DateTime? expiry = DateTime.TryParse(expiresAt, out var dt) ? dt.ToUniversalTime() : null;

        var results = new List<DocumentResult>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var doc = new Document
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                Category = cat,
                Tags = tags,
                SourceModule = sourceModule,
                SourceRefId = refId,
                ExpiresAt = expiry,
                Notes = notes,
            };

            using var stream = file.OpenReadStream();
            doc.StoragePath = await storage.SaveAsync(cat, doc.Id, file.FileName, stream);

            var saved = await repo.AddAsync(doc);
            results.Add(ToResult(saved));
        }

        return Ok(results);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var doc = await repo.GetAsync(id);
        if (doc is null) return NotFound();

        var fullPath = storage.GetFullPath(doc.StoragePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound("File missing from storage.");

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, string.IsNullOrEmpty(doc.ContentType) ? "application/octet-stream" : doc.ContentType, doc.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var doc = await repo.GetAsync(id);
        if (doc is null) return NotFound();

        var ok = await repo.DeleteAsync(id);
        if (ok) storage.Delete(doc.StoragePath);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("expiring")]
    public async Task<IActionResult> Expiring([FromQuery] int days = 30)
    {
        var docs = await repo.GetExpiringAsync(days);
        return Ok(docs.Select(ToResult));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var s = await repo.GetStatsAsync();
        return Ok(new StatsResult(
            s.TotalCount,
            FormatBytes(s.TotalBytes),
            s.ByCategory,
            s.ExpiringSoon
        ));
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    private static DocumentResult ToResult(Document d) => new(
        d.Id, d.FileName, d.ContentType, d.SizeBytes, d.Category,
        d.Tags, d.SourceModule, d.SourceRefId, d.ExpiresAt, d.Notes, d.UploadedAt
    );
}
