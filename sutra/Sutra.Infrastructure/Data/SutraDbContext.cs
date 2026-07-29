using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sutra.Domain.Entities;

namespace Sutra.Infrastructure.Data;

public class SutraDbContext(DbContextOptions<SutraDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

    // SQLite has no timezone-aware column type — every DateTime loses its Kind on
    // round-trip and comes back Unspecified, which System.Text.Json then serializes
    // without a 'Z' suffix, causing frontend clients to misparse UTC instants as
    // local time. All DateTime columns here are UTC instants — re-tag Kind=Utc on read.
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => d.Category);
            e.HasIndex(d => d.SourceModule);
            e.HasIndex(d => d.ExpiresAt);
            e.HasIndex(d => d.UploadedAt);
        });
    }
}
