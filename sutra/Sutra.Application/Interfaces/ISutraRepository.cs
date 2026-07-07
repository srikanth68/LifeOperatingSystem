using Sutra.Domain.Entities;

namespace Sutra.Application.Interfaces;

public interface ISutraRepository
{
    Task<Document> AddAsync(Document doc);
    Task<Document?> GetAsync(Guid id);
    Task<List<Document>> ListAsync(string? category = null, string? sourceModule = null, string? query = null);
    Task<bool> DeleteAsync(Guid id);
    Task<List<Document>> GetExpiringAsync(int withinDays);
    Task<DocumentStats> GetStatsAsync();
}

public record DocumentStats(
    int TotalCount,
    long TotalBytes,
    Dictionary<string, int> ByCategory,
    int ExpiringSoon
);
