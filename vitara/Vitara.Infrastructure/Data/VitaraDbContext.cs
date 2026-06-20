using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Vitara.Domain.Entities;

namespace Vitara.Infrastructure.Data;

public class VitaraDbContext(DbContextOptions<VitaraDbContext> options) : DbContext(options)
{
    // ISO-8601, zero-padded — sorts identically whether compared as text (SQLite has no
    // native date type) or as a date. The previous d.ToString()/DateOnly.Parse(s) pair used
    // the current culture's short-date pattern (e.g. "6/9/2026", no leading zeros), which
    // SQLite compares lexicographically: "6/9/2026" > "6/15/2026" and "6/9/2026" > "6/14/2026"
    // as strings even though those dates are earlier. That made `Day >= from && Day <= to`
    // range filters silently drop rows whenever `from` landed on a single-digit day in the
    // same month as a stored double-digit day — exactly the "days=14/7/8/1 return []" bug.
    public const string DayFormat = "yyyy-MM-dd";

    public DbSet<OuraToken>      Tokens     => Set<OuraToken>();
    public DbSet<SleepSession>   Sleep      => Set<SleepSession>();
    public DbSet<DailyReadiness> Readiness  => Set<DailyReadiness>();
    public DbSet<DailyActivity>  Activity   => Set<DailyActivity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<OuraToken>().HasKey(t => t.Id);

        b.Entity<SleepSession>().HasKey(s => s.Id);
        b.Entity<SleepSession>().Property(s => s.Day).HasConversion(
            d => d.ToString(DayFormat, CultureInfo.InvariantCulture),
            s => DateOnly.ParseExact(s, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));

        b.Entity<DailyReadiness>().HasKey(r => r.Id);
        b.Entity<DailyReadiness>().Property(r => r.Day).HasConversion(
            d => d.ToString(DayFormat, CultureInfo.InvariantCulture),
            s => DateOnly.ParseExact(s, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));

        b.Entity<DailyActivity>().HasKey(a => a.Id);
        b.Entity<DailyActivity>().Property(a => a.Day).HasConversion(
            d => d.ToString(DayFormat, CultureInfo.InvariantCulture),
            s => DateOnly.ParseExact(s, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None));
    }
}
