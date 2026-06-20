namespace Aasthi.Domain.Entities;

public class PropertyDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }

    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Category { get; set; } = "other"; // deed, insurance, lease, tax, inspection, other
    public string StoragePath { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
