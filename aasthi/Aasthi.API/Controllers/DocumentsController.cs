using Microsoft.AspNetCore.Mvc;
using Aasthi.Application.DTOs;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

[ApiController, Route("api/properties/{propertyId:guid}/documents")]
public class DocumentsController(IAasthiRepository repo, IDocumentStorage storage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(Guid propertyId)
    {
        var property = await repo.GetPropertyAsync(propertyId);
        if (property is null) return NotFound();
        return Ok(property.Documents.Select(ToResult));
    }

    // Bulk upload: accepts one or more files in a single multipart/form-data request.
    [HttpPost]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Upload(Guid propertyId, [FromForm] IFormFileCollection files, [FromForm] string? category)
    {
        var property = await repo.GetPropertyAsync(propertyId);
        if (property is null) return NotFound("Property not found.");
        if (files.Count == 0) return BadRequest("No files provided.");

        var results = new List<DocumentResult>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var document = new PropertyDocument
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                Category = string.IsNullOrWhiteSpace(category) ? "other" : category,
            };

            using var stream = file.OpenReadStream();
            document.StoragePath = await storage.SaveAsync(propertyId, document.Id, file.FileName, stream);

            var saved = await repo.AddDocumentAsync(propertyId, document);
            if (saved is not null) results.Add(ToResult(saved));
        }

        return Ok(results);
    }

    [HttpGet("{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid propertyId, Guid documentId)
    {
        var doc = await repo.GetDocumentAsync(propertyId, documentId);
        if (doc is null) return NotFound();

        var fullPath = storage.GetFullPath(doc.StoragePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound("File missing from storage.");

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, string.IsNullOrEmpty(doc.ContentType) ? "application/octet-stream" : doc.ContentType, doc.FileName);
    }

    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(Guid propertyId, Guid documentId)
    {
        var doc = await repo.GetDocumentAsync(propertyId, documentId);
        if (doc is null) return NotFound();

        var ok = await repo.DeleteDocumentAsync(propertyId, documentId);
        if (ok) storage.Delete(doc.StoragePath);
        return ok ? NoContent() : NotFound();
    }

    private static DocumentResult ToResult(PropertyDocument d) =>
        new(d.Id, d.PropertyId, d.FileName, d.ContentType, d.SizeBytes, d.Category, d.UploadedAt);
}
