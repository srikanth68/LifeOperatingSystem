using Microsoft.EntityFrameworkCore;
using Sutra.Application.Interfaces;
using Sutra.Domain.Entities;

namespace Sutra.Infrastructure.Data;

public class SutraRepository(SutraDbContext db) : ISutraRepository
{
    public async Task<Document> AddAsync(Document doc)
    {
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<Document?> GetAsync(Guid id) =>
        await db.Documents.FindAsync(id);

    public async Task<Document?> UpdateAsync(Guid id, Action<Document> mutate)
    {
        var doc = await db.Documents.FindAsync(id);
        if (doc is null) return null;
        mutate(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    public async Task<List<Document>> ListAsync(string? category = null, string? sourceModule = null, string? query = null)
    {
        var q = db.Documents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(d => d.Category == category);

        if (!string.IsNullOrWhiteSpace(sourceModule))
            q = q.Where(d => d.SourceModule == sourceModule);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var lower = query.ToLower();
            q = q.Where(d => d.FileName.ToLower().Contains(lower)
                           || (d.Tags != null && d.Tags.ToLower().Contains(lower))
                           || (d.Notes != null && d.Notes.ToLower().Contains(lower)));
        }

        return await q.OrderByDescending(d => d.UploadedAt).ToListAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var doc = await db.Documents.FindAsync(id);
        if (doc is null) return false;
        db.Documents.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<Document>> GetExpiringAsync(int withinDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(withinDays);
        return await db.Documents
            .Where(d => d.ExpiresAt != null && d.ExpiresAt <= cutoff && d.ExpiresAt >= DateTime.UtcNow)
            .OrderBy(d => d.ExpiresAt)
            .ToListAsync();
    }

    public async Task<DocumentStats> GetStatsAsync()
    {
        var docs = await db.Documents.ToListAsync();
        var byCategory = docs.GroupBy(d => d.Category).ToDictionary(g => g.Key, g => g.Count());
        var cutoff = DateTime.UtcNow.AddDays(30);
        var expiring = docs.Count(d => d.ExpiresAt != null && d.ExpiresAt <= cutoff && d.ExpiresAt >= DateTime.UtcNow);

        return new DocumentStats(
            docs.Count,
            docs.Sum(d => d.SizeBytes),
            byCategory,
            expiring
        );
    }
}
