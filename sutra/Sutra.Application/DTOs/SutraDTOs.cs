namespace Sutra.Application.DTOs;

public record DocumentResult(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Category,
    string? Tags,
    string? SourceModule,
    Guid? SourceRefId,
    DateTime? ExpiresAt,
    string? Notes,
    DateTime UploadedAt
);

public record UploadRequest(
    string? Category,
    string? Tags,
    string? SourceModule,
    Guid? SourceRefId,
    DateTime? ExpiresAt,
    string? Notes
);

public record StatsResult(
    int TotalCount,
    string TotalSize,
    Dictionary<string, int> ByCategory,
    int ExpiringSoon
);
