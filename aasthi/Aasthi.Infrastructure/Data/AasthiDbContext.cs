using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Aasthi.Domain.Entities;

namespace Aasthi.Infrastructure.Data;

public class AasthiDbContext(DbContextOptions<AasthiDbContext> options) : DbContext(options)
{
    // ISO-8601, zero-padded — sorts correctly as TEXT in SQLite. See Vitara's DbContext for
    // the bug this avoids: culture-default DateOnly.ToString() omits leading zeros, which
    // breaks lexicographic range comparisons.
    public const string DateFormat = "yyyy-MM-dd";

    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyContact> Contacts => Set<PropertyContact>();
    public DbSet<PropertyDocument> Documents => Set<PropertyDocument>();

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
    }
}
