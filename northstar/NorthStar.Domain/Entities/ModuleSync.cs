namespace NorthStar.Domain.Entities;

public class ModuleSync
{
    public string Module { get; set; } = ""; // vault, vitara, aasthi, san
    public DateTime LastSyncAt { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
}
