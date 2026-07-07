using Microsoft.EntityFrameworkCore;
using Sutra.Domain.Entities;

namespace Sutra.Infrastructure.Data;

public class SutraDbContext(DbContextOptions<SutraDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();

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
