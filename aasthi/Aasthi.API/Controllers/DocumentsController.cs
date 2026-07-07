using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Aasthi.Application.DTOs;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

[ApiController, Route("api/properties/{propertyId:guid}/documents")]
public class DocumentsController(IAasthiRepository repo, IHttpClientFactory httpFactory, IDocumentStorage storage) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid propertyId)
    {
        var property = await repo.GetPropertyAsync(propertyId);
        if (property is null) return NotFound();
        return Ok(property.Documents.Select(ToResult));
    }

    [HttpPost]
    [RequestSizeLimit(200_000_000)]
    public async Task<IActionResult> Upload(Guid propertyId, [FromForm] IFormFileCollection files, [FromForm] string? category)
    {
        var property = await repo.GetPropertyAsync(propertyId);
        if (property is null) return NotFound("Property not found.");
        if (files.Count == 0) return BadRequest("No files provided.");

        var cat = string.IsNullOrWhiteSpace(category) ? "other" : category;
        var results = new List<DocumentResult>();

        foreach (var file in files)
        {
            if (file.Length == 0) continue;

            var sutraId = await UploadToSutra(file, cat, propertyId);

            var document = new PropertyDocument
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                Category = cat,
                SutraDocumentId = sutraId,
            };

            if (sutraId is null)
            {
                using var stream = file.OpenReadStream();
                document.StoragePath = await storage.SaveAsync(propertyId, document.Id, file.FileName, stream);
            }

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

        if (doc.SutraDocumentId.HasValue)
        {
            var sutra = httpFactory.CreateClient("sutra");
            var resp = await sutra.GetAsync($"/api/documents/{doc.SutraDocumentId}/download");
            if (!resp.IsSuccessStatusCode) return NotFound("File missing from Sutra.");
            var stream = await resp.Content.ReadAsStreamAsync();
            return File(stream, doc.ContentType ?? "application/octet-stream", doc.FileName);
        }

        var fullPath = storage.GetFullPath(doc.StoragePath);
        if (!System.IO.File.Exists(fullPath)) return NotFound("File missing from storage.");
        var fileStream = System.IO.File.OpenRead(fullPath);
        return File(fileStream, string.IsNullOrEmpty(doc.ContentType) ? "application/octet-stream" : doc.ContentType, doc.FileName);
    }

    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(Guid propertyId, Guid documentId)
    {
        var doc = await repo.GetDocumentAsync(propertyId, documentId);
        if (doc is null) return NotFound();

        if (doc.SutraDocumentId.HasValue)
        {
            var sutra = httpFactory.CreateClient("sutra");
            await sutra.DeleteAsync($"/api/documents/{doc.SutraDocumentId}");
        }
        else if (!string.IsNullOrEmpty(doc.StoragePath))
        {
            storage.Delete(doc.StoragePath);
        }

        var ok = await repo.DeleteDocumentAsync(propertyId, documentId);
        return ok ? NoContent() : NotFound();
    }

    private async Task<Guid?> UploadToSutra(IFormFile file, string category, Guid propertyId)
    {
        try
        {
            var sutra = httpFactory.CreateClient("sutra");
            using var content = new MultipartFormDataContent();
            using var stream = file.OpenReadStream();
            content.Add(new StreamContent(stream), "files", file.FileName);
            content.Add(new StringContent(MapCategory(category)), "category");
            content.Add(new StringContent("aasthi"), "sourceModule");
            content.Add(new StringContent(propertyId.ToString()), "sourceRefId");

            var resp = await sutra.PostAsync("/api/documents", content);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            var docs = JsonSerializer.Deserialize<List<SutraDocResult>>(body, JsonOpts);
            return docs?.FirstOrDefault()?.Id;
        }
        catch
        {
            return null;
        }
    }

    private static string MapCategory(string aasthiCategory) => aasthiCategory switch
    {
        "deed" or "lease" => "property",
        "insurance" => "insurance",
        "tax" => "finance",
        "inspection" => "property",
        _ => "property",
    };

    private static DocumentResult ToResult(PropertyDocument d) =>
        new(d.Id, d.PropertyId, d.FileName, d.ContentType, d.SizeBytes, d.Category, d.UploadedAt);

    private record SutraDocResult(Guid Id, string FileName);
}
