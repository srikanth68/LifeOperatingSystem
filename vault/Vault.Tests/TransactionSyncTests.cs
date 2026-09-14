using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vault.Worker.Data;
using Vault.Worker.Models;
using Vault.Worker.Services;

namespace Vault.Tests;

// One purchase, one row. These pin down how a pending charge becomes its posted
// transaction, and what the reviewed cleanup is and is not allowed to remove.
public class TransactionSyncTests
{
    private sealed class FakePlaid : IPlaidService
    {
        public List<PlaidAccountData> Accounts { get; } = [new("acc-1", "Checking", "depository", "checking", 1000m, 900m, "USD")];
        public List<PlaidTransactionData> Transactions { get; set; } = [];
        public bool Fail { get; set; }

        public Task<List<PlaidAccountData>> GetAccountsAsync(string accessToken) => Task.FromResult(Accounts);
        public Task<List<PlaidTransactionData>> GetTransactionsAsync(string accessToken, DateTime startDate, DateTime endDate) =>
            Fail ? throw new InvalidOperationException("Plaid transactions/get failed with HTTP 500.")
                 : Task.FromResult(Transactions);
        public Task<PlaidInstitutionInfo?> GetInstitutionInfoAsync(string plaidInstitutionId) =>
            Task.FromResult<PlaidInstitutionInfo?>(new("ins-1", "Test Bank", null, null));
        public Task<string> CreateLinkTokenAsync() => throw new NotSupportedException();
        public Task<(string accessToken, string itemId)> ExchangePublicTokenAsync(string publicToken) => throw new NotSupportedException();
        public Task<bool> ValidateCredentialsAsync() => Task.FromResult(true);
    }

    private static async Task<(SqliteConnection Conn, VaultDbContext Db, FakePlaid Plaid, SyncService Sync)> NewAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var db = new VaultDbContext(new DbContextOptionsBuilder<VaultDbContext>().UseSqlite(conn).Options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        db.PlaidItems.Add(new PlaidItem
        {
            Id = "item", PlaidItemId = "plaid-item", AccessToken = "token", PlaidInstitutionId = "ins-1",
            InstitutionName = "Test Bank", IsActive = true, CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var plaid = new FakePlaid();
        return (conn, db, plaid, new SyncService(plaid, db, NullLogger<SyncService>.Instance));
    }

    private static PlaidTransactionData Txn(string id, decimal amount, int daysAgo, bool pending = false, string? pendingId = null) =>
        new(id, "acc-1", amount, "USD", DateTime.UtcNow.Date.AddDays(-daysAgo), "COFFEE SHOP", "Coffee Shop", "Food and Drink",
            pending, pendingId);

    private static async Task<Transaction> AddRow(VaultDbContext db, string plaidId, decimal amount, int daysAgo, bool pending = false)
    {
        var accountId = await db.Accounts.Select(a => a.Id).SingleAsync();
        var row = new Transaction
        {
            Id = Guid.NewGuid().ToString(), PlaidTransactionId = plaidId, AccountId = accountId, Amount = amount,
            TransactionDate = DateTime.UtcNow.Date.AddDays(-daysAgo), Description = "COFFEE SHOP", IsPending = pending,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Transactions.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    // ── Sync ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APostedChargeTakesOverItsPendingRow_AndKeepsTheUsersCategory()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;

        plaid.Transactions = [Txn("pend-1", 12.50m, daysAgo: 2, pending: true)];
        await sync.SyncAsync();

        var pendingRow = await db.Transactions.SingleAsync();
        Assert.True(pendingRow.IsPending);
        pendingRow.Category = "Dining out";
        await db.SaveChangesAsync();

        plaid.Transactions = [Txn("post-1", 14.00m, daysAgo: 1, pendingId: "pend-1")];
        await sync.SyncAsync();

        db.ChangeTracker.Clear();
        var row = await db.Transactions.SingleAsync();
        Assert.Equal("post-1", row.PlaidTransactionId);
        Assert.False(row.IsPending);
        Assert.Equal(14.00m, row.Amount);          // the tip landed
        Assert.Equal("Dining out", row.Category);  // the user's category survived
    }

    [Fact]
    public async Task APendingChargePlaidStopsReturningIsRemoved()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;

        plaid.Transactions = [Txn("pend-cancelled", 40m, daysAgo: 3, pending: true), Txn("post-other", 9m, daysAgo: 3)];
        await sync.SyncAsync();
        Assert.Equal(2, await db.Transactions.CountAsync());

        plaid.Transactions = [Txn("post-other", 9m, daysAgo: 3)];
        await sync.SyncAsync();

        db.ChangeTracker.Clear();
        Assert.Equal("post-other", (await db.Transactions.SingleAsync()).PlaidTransactionId);
    }

    [Fact]
    public async Task SyncNeverRemovesAPostedRow()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;

        plaid.Transactions = [Txn("post-1", 20m, daysAgo: 5)];
        await sync.SyncAsync();

        plaid.Transactions = [];   // missing from this answer, for whatever reason
        await sync.SyncAsync();

        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task APendingRowOlderThanTheSyncWindowIsLeftAlone()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;
        await sync.SyncAsync();                         // creates the account
        await AddRow(db, "old-pending", 5m, daysAgo: 60, pending: true);

        plaid.Transactions = [Txn("post-1", 20m, daysAgo: 5)];
        await sync.SyncAsync();

        Assert.True(await db.Transactions.AnyAsync(t => t.PlaidTransactionId == "old-pending"));
    }

    [Fact]
    public async Task APlaidFailureRemovesNothing()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;

        plaid.Transactions = [Txn("pend-1", 12m, daysAgo: 1, pending: true)];
        await sync.SyncAsync();

        plaid.Fail = true;
        await sync.SyncAsync();

        Assert.True(await db.Transactions.AnyAsync(t => t.PlaidTransactionId == "pend-1"));
    }

    // ── Reviewed cleanup ─────────────────────────────────────────────────────

    // The rows already in Vault: a pending copy stored as posted by the old sync, beside
    // the real posted transaction Plaid still returns.
    [Fact]
    public async Task TheCleanupListsALegacyPendingCopyBesideItsPostedTwin()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;
        plaid.Transactions = [Txn("post-1", 14m, daysAgo: 10), Txn("anchor", 3m, daysAgo: 90)];
        await sync.SyncAsync();
        var legacy = await AddRow(db, "pend-legacy", 12.50m, daysAgo: 12);

        var stale = await sync.FindStaleTransactionsAsync();

        var s = Assert.Single(stale);
        Assert.Equal(legacy.Id, s.Id);
        Assert.True(s.LikelyPendingCopy);
        Assert.NotNull(s.PostedTwinId);
    }

    [Fact]
    public async Task TheCleanupIgnoresHistoryOlderThanPlaidReturned()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;
        plaid.Transactions = [Txn("post-1", 14m, daysAgo: 10)];
        await sync.SyncAsync();
        await AddRow(db, "from-before-plaids-history", 30m, daysAgo: 400);

        Assert.Empty(await sync.FindStaleTransactionsAsync());
    }

    [Fact]
    public async Task AnEmptyPlaidHistoryFlagsNothing()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;
        plaid.Transactions = [Txn("post-1", 14m, daysAgo: 10)];
        await sync.SyncAsync();

        plaid.Transactions = [];
        Assert.Empty(await sync.FindStaleTransactionsAsync());
    }

    [Fact]
    public async Task RemovingDeletesOnlyRowsThatAreStillStale()
    {
        var (conn, db, plaid, sync) = await NewAsync();
        using var _ = conn;
        plaid.Transactions = [Txn("post-1", 14m, daysAgo: 10), Txn("anchor", 3m, daysAgo: 90)];
        await sync.SyncAsync();
        var legacy = await AddRow(db, "pend-legacy", 12.50m, daysAgo: 12);
        var real = await db.Transactions.SingleAsync(t => t.PlaidTransactionId == "post-1");

        // A reviewed list that also (wrongly) includes a live transaction.
        var removed = await sync.RemoveStaleTransactionsAsync([legacy.Id, real.Id]);

        Assert.Equal(1, removed);
        Assert.False(await db.Transactions.AnyAsync(t => t.Id == legacy.Id));
        Assert.True(await db.Transactions.AnyAsync(t => t.Id == real.Id));
    }
}
