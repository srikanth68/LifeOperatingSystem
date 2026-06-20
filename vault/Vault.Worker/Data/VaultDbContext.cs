using Microsoft.EntityFrameworkCore;
using Vault.Worker.Models;

namespace Vault.Worker.Data;

public class VaultDbContext : DbContext
{
    public VaultDbContext(DbContextOptions<VaultDbContext> options) : base(options) { }

    public DbSet<Institution> Institutions { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<SyncMetadata> SyncMetadata { get; set; }
    public DbSet<PlaidItem> PlaidItems { get; set; }
    public DbSet<CategoryGroup> CategoryGroups { get; set; }
    public DbSet<CategoryGroupItem> CategoryGroupItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Institution
        modelBuilder.Entity<Institution>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("lower(hex(randomblob(16)))");
            b.HasIndex(x => x.PlaidInstitutionId).IsUnique();
        });

        // Account
        modelBuilder.Entity<Account>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("lower(hex(randomblob(16)))");
            b.HasIndex(x => x.PlaidAccountId).IsUnique();
            b.HasIndex(x => x.InstitutionId);
            b.HasOne(x => x.Institution)
                .WithMany(x => x.Accounts)
                .HasForeignKey(x => x.InstitutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Transaction
        modelBuilder.Entity<Transaction>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("lower(hex(randomblob(16)))");
            b.HasIndex(x => x.PlaidTransactionId).IsUnique();
            b.HasIndex(x => x.AccountId);
            b.HasIndex(x => x.TransactionDate);
            b.HasOne(x => x.Account)
                .WithMany(x => x.Transactions)
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SyncMetadata
        modelBuilder.Entity<SyncMetadata>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("lower(hex(randomblob(16)))");
            b.HasIndex(x => x.LastSyncTime);
        });

        // PlaidItem
        modelBuilder.Entity<PlaidItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("lower(hex(randomblob(16)))");
            b.HasIndex(x => x.PlaidItemId).IsUnique();
        });

        // CategoryGroup
        modelBuilder.Entity<CategoryGroup>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("lower(hex(randomblob(16)))");
        });

        // CategoryGroupItem
        modelBuilder.Entity<CategoryGroupItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasDefaultValueSql("lower(hex(randomblob(16)))");
            b.HasOne(x => x.Group)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.CategoryGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
