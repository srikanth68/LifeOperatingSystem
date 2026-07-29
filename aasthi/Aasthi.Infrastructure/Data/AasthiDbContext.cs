using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Aasthi.Domain.Entities;

namespace Aasthi.Infrastructure.Data;

public class AasthiDbContext(DbContextOptions<AasthiDbContext> options) : DbContext(options)
{
    // ISO-8601, zero-padded — sorts correctly as TEXT in SQLite. See Vitara's DbContext for
    // the bug this avoids: culture-default DateOnly.ToString() omits leading zeros, which
    // breaks lexicographic range comparisons.
    public const string DateFormat = "yyyy-MM-dd";

    // Separate bug: SQLite has no timezone-aware column type, so every DateTime (not
    // DateOnly — those are handled above) loses its Kind on round-trip and comes back
    // Unspecified, which System.Text.Json then serializes without a 'Z' suffix, causing
    // frontend clients to misparse UTC instants as local time. CreatedAt/UploadedAt/etc.
    // below are UTC instants — re-tag Kind=Utc on read.
    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyContact> Contacts => Set<PropertyContact>();
    public DbSet<PropertyDocument> Documents => Set<PropertyDocument>();
    public DbSet<PropertyTask> Tasks => Set<PropertyTask>();
    public DbSet<PropertyFinancialEntry> FinancialEntries => Set<PropertyFinancialEntry>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Property>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.PurchaseDate).HasConversion(
                d => d.HasValue ? d.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : null,
                s => string.IsNullOrEmpty(s) ? null : DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
            e.Property(p => p.CurrentValueAsOf).HasConversion(
                d => d.HasValue ? d.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : null,
                s => string.IsNullOrEmpty(s) ? null : DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
            e.Property(p => p.PurchasePrice).HasColumnType("decimal(18,2)");
            e.Property(p => p.CurrentValue).HasColumnType("decimal(18,2)");
            e.Ignore(p => p.ProfitAmount);
            e.Ignore(p => p.ProfitPct);

            e.HasMany(p => p.Contacts).WithOne(c => c.Property).HasForeignKey(c => c.PropertyId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Documents).WithOne(d => d.Property).HasForeignKey(d => d.PropertyId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PropertyContact>().HasKey(c => c.Id);
        b.Entity<PropertyDocument>().HasKey(d => d.Id);

        b.Entity<PropertyTask>(t =>
        {
            t.HasKey(x => x.Id);
            t.Property(x => x.DueDate).HasConversion(
                d => d.HasValue ? d.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : null,
                s => string.IsNullOrEmpty(s) ? null : DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
            t.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
            t.HasIndex(x => new { x.PropertyId, x.Status });
        });

        b.Entity<PropertyFinancialEntry>(f =>
        {
            f.HasKey(x => x.Id);
            f.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            f.Property(x => x.Date).HasConversion(
                d => d.ToString(DateFormat, CultureInfo.InvariantCulture),
                s => DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
            f.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
            f.HasIndex(x => new { x.PropertyId, x.Type });
        });

        b.Entity<MaintenanceLog>(m =>
        {
            m.HasKey(x => x.Id);
            m.Property(x => x.Cost).HasColumnType("decimal(18,2)");
            m.Property(x => x.CompletedDate).HasConversion(
                d => d.HasValue ? d.Value.ToString(DateFormat, CultureInfo.InvariantCulture) : null,
                s => string.IsNullOrEmpty(s) ? null : DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
            m.HasOne(x => x.Property).WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
            m.HasIndex(x => new { x.PropertyId, x.Category });
        });
    }
}
