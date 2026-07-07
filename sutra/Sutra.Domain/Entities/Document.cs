namespace Sutra.Domain.Entities;

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Category { get; set; } = "other";
    public string? Tags { get; set; }
    public string? SourceModule { get; set; }
    public Guid? SourceRefId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string StoragePath { get; set; } = "";
    public string? Notes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
