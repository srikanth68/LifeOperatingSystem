using Microsoft.EntityFrameworkCore;
using Vault.Worker.Data;
using Vault.Worker.Models;

namespace Vault.Worker.Services;

public interface ISyncService
{
    Task<SyncMetadata> SyncAsync();

    // Rows Vault holds that Plaid no longer returns, over up to two years of history, for
    // the user to review. Read-only.
    Task<List<StaleTransaction>> FindStaleTransactionsAsync(int days = 730);

    // Deletes the given rows -- but only those that are STILL stale when re-checked
    // against Plaid at the moment of deletion. Returns how many were removed.
    Task<int> RemoveStaleTransactionsAsync(IReadOnlyCollection<string> ids, int days = 730);
}

// A row Plaid no longer has. LikelyPendingCopy marks one with a posted twin nearby: same
// account, similar amount, within a week -- the shape of a pending charge that posted
// under a new id before sync knew how to follow it.
public record StaleTransaction(
    string Id, string AccountName, DateTime Date, decimal Amount, string Description,
    bool LikelyPendingCopy, string? PostedTwinId);

public class SyncService : ISyncService
{
    private readonly IPlaidService _plaidService;
    private readonly VaultDbContext _db;
    private readonly ILogger<SyncService> _logger;

    public SyncService(IPlaidService plaidService, VaultDbContext db, ILogger<SyncService> logger)
    {
        _plaidService = plaidService;
        _db = db;
        _logger = logger;
    }

    public async Task<SyncMetadata> SyncAsync()
    {
        var syncRecord = new SyncMetadata
        {
            Id = Guid.NewGuid().ToString(),
            LastSyncTime = DateTime.UtcNow,
            Status = "in_progress",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var transactionsAdded = 0;

        try
        {
            _logger.LogInformation("Starting sync...");

            var items = await _db.PlaidItems.Where(i => i.IsActive).ToListAsync();
            if (!items.Any())
            {
                _logger.LogInformation("No linked accounts — sync complete with no data.");
                syncRecord.Status = "completed";
                syncRecord.TransactionsAdded = 0;
                // fall through to finally which saves syncRecord
                return syncRecord;
            }

            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-30);

            foreach (var item in items)
            {
                try
                {
                    // Sync accounts for this item
                    var plaidAccounts = await _plaidService.GetAccountsAsync(item.AccessToken);
                    var institution = await EnsureInstitutionAsync(item);

                    foreach (var pa in plaidAccounts)
                    {
                        await UpsertAccountAsync(pa, institution.Id);
                    }
                    await _db.SaveChangesAsync();

                    // Sync transactions
                    var plaidTxns = await _plaidService.GetTransactionsAsync(item.AccessToken, startDate, endDate);
                    var (added, posted, removed) = await ApplyTransactionsAsync(
                        plaidAccounts.Select(a => a.AccountId).ToList(), plaidTxns, startDate);
                    transactionsAdded += added;

                    _logger.LogInformation(
                        "Synced item {ItemId}: {Accounts} accounts, {Txns} transactions ({Added} new, {Posted} pending posted, {Removed} pending dropped)",
                        item.PlaidItemId, plaidAccounts.Count, plaidTxns.Count, added, posted, removed);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync item {ItemId}", item.PlaidItemId);
                }
            }

            syncRecord.Status = "completed";
            syncRecord.TransactionsAdded = transactionsAdded;
        }
        catch (Exception ex)
        {
            syncRecord.Status = "failed";
            syncRecord.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Sync failed");
        }
        finally
        {
            syncRecord.UpdatedAt = DateTime.UtcNow;
            _db.SyncMetadata.Add(syncRecord);
            await _db.SaveChangesAsync();
        }

        return syncRecord;
    }

    // Writes one item's transactions for the sync window, keeping ONE row per purchase.
    //
    // Duplicates came from pending charges. Plaid returns a pending charge with its own
    // transaction_id; when it posts, Plaid issues a NEW id for the posted version, points
    // it back at the pending one, and stops returning the pending one. Sync stored every
    // id it had not seen, marked everything not-pending, and never removed anything --
    // so every purchase still pending during a sync stayed in Vault twice.
    //
    // Now: a posted transaction takes over its pending row (keeping any category the user
    // set on it), and pending rows in the window that Plaid no longer returns are removed,
    // since they either posted under a new id or were cancelled. Posted rows are never
    // removed here; that is the reviewed cleanup's job.
    public async Task<(int Added, int Posted, int Removed)> ApplyTransactionsAsync(
        IReadOnlyCollection<string> itemPlaidAccountIds,
        IReadOnlyList<PlaidTransactionData> plaidTxns,
        DateTime windowStart)
    {
        var accountIds = await _db.Accounts
            .Where(a => itemPlaidAccountIds.Contains(a.PlaidAccountId))
            .ToDictionaryAsync(a => a.PlaidAccountId, a => a.Id);

        int added = 0, posted = 0, removed = 0;
        var now = DateTime.UtcNow;

        foreach (var pt in plaidTxns)
        {
            if (!accountIds.TryGetValue(pt.AccountId, out var accountId)) continue;

            var row = await _db.Transactions.FirstOrDefaultAsync(t => t.PlaidTransactionId == pt.TransactionId);

            if (row is null && pt.PendingTransactionId is { Length: > 0 } pendingId)
            {
                row = await _db.Transactions.FirstOrDefaultAsync(t => t.PlaidTransactionId == pendingId);
                if (row is not null)
                {
                    row.PlaidTransactionId = pt.TransactionId;
                    posted++;
                }
            }

            if (row is null)
            {
                _db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid().ToString(),
                    PlaidTransactionId = pt.TransactionId,
                    AccountId = accountId,
                    Amount = pt.Amount,
                    Currency = pt.Currency,
                    TransactionDate = pt.Date,
                    Description = pt.Name,
                    MerchantName = pt.MerchantName,
                    Category = pt.Category,
                    IsPending = pt.Pending,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                added++;
                continue;
            }

            // Plaid is the source of truth for what the charge is. The category is the
            // user's, once they have set one.
            row.AccountId = accountId;
            row.Amount = pt.Amount;
            row.Currency = pt.Currency;
            row.TransactionDate = pt.Date;
            row.Description = pt.Name;
            row.MerchantName = pt.MerchantName ?? row.MerchantName;
            row.Category ??= pt.Category;
            row.IsPending = pt.Pending;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();

        var returned = plaidTxns.Select(t => t.TransactionId).ToHashSet();
        var itemAccounts = accountIds.Values.ToList();
        var pendingInWindow = await _db.Transactions
            .Where(t => t.IsPending && itemAccounts.Contains(t.AccountId) && t.TransactionDate >= windowStart)
            .ToListAsync();

        foreach (var stale in pendingInWindow.Where(t => !returned.Contains(t.PlaidTransactionId)))
        {
            _db.Transactions.Remove(stale);
            removed++;
        }
        await _db.SaveChangesAsync();

        return (added, posted, removed);
    }

    public async Task<List<StaleTransaction>> FindStaleTransactionsAsync(int days = 730)
    {
        var end = DateTime.UtcNow.Date;
        var start = end.AddDays(-Math.Clamp(days, 1, 730));
        var result = new List<StaleTransaction>();

        foreach (var item in await _db.PlaidItems.Where(i => i.IsActive).ToListAsync())
        {
            // Any failure throws and ends the whole check. A half-answered comparison would
            // list real transactions as stale.
            var plaidAccountIds = (await _plaidService.GetAccountsAsync(item.AccessToken)).Select(a => a.AccountId).ToList();
            var accounts = await _db.Accounts.Where(a => plaidAccountIds.Contains(a.PlaidAccountId)).ToListAsync();
            if (accounts.Count == 0) continue;

            var txns = await _plaidService.GetTransactionsAsync(item.AccessToken, start, end);

            // An empty history is far more likely a quiet failure than an account whose
            // every transaction vanished. Flag nothing rather than everything.
            if (txns.Count == 0) continue;

            var returned = txns.Select(t => t.TransactionId).ToHashSet();

            // Institutions keep different amounts of history. Nothing older than the oldest
            // transaction Plaid actually returned is judged, since Plaid can't vouch for it.
            var oldest = txns.Min(t => t.Date);
            var accountIds = accounts.Select(a => a.Id).ToList();
            var names = accounts.ToDictionary(a => a.Id, a => a.Name);

            var local = await _db.Transactions
                .Where(t => accountIds.Contains(t.AccountId) && t.TransactionDate >= oldest)
                .AsNoTracking()
                .ToListAsync();

            var kept = local.Where(t => returned.Contains(t.PlaidTransactionId)).ToList();

            foreach (var t in local.Where(t => !returned.Contains(t.PlaidTransactionId)))
            {
                // Tips and final amounts move a posted charge off its pending amount, so the
                // twin match allows a quarter either way (at least a dollar).
                var tolerance = Math.Max(1m, Math.Abs(t.Amount) * 0.25m);
                var twin = kept.FirstOrDefault(k =>
                    k.AccountId == t.AccountId
                    && Math.Abs(k.Amount - t.Amount) <= tolerance
                    && Math.Abs((k.TransactionDate - t.TransactionDate).TotalDays) <= 7);

                result.Add(new StaleTransaction(
                    t.Id, names[t.AccountId], t.TransactionDate, t.Amount, t.Description,
                    twin is not null, twin?.Id));
            }
        }

        return result.OrderByDescending(s => s.Date).ToList();
    }

    public async Task<int> RemoveStaleTransactionsAsync(IReadOnlyCollection<string> ids, int days = 730)
    {
        if (ids.Count == 0) return 0;

        // Re-checked now, not trusted from the list the user reviewed: a transaction that
        // Plaid has started returning again since then must survive the click.
        var stillStale = (await FindStaleTransactionsAsync(days)).Select(s => s.Id).ToHashSet();
        var toRemove = ids.Where(stillStale.Contains).Distinct().ToList();
        if (toRemove.Count == 0) return 0;

        var rows = await _db.Transactions.Where(t => toRemove.Contains(t.Id)).ToListAsync();
        _db.Transactions.RemoveRange(rows);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Removed {Count} stale transaction(s) after review.", rows.Count);
        return rows.Count;
    }

    private async Task<Institution> EnsureInstitutionAsync(PlaidItem item)
    {
        var existing = await _db.Institutions
            .FirstOrDefaultAsync(i => i.PlaidInstitutionId == item.PlaidInstitutionId);

        if (existing != null)
        {
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existing;
        }

        var info = await _plaidService.GetInstitutionInfoAsync(item.PlaidInstitutionId);
        var institution = new Institution
        {
            Id = Guid.NewGuid().ToString(),
            PlaidInstitutionId = item.PlaidInstitutionId,
            Name = info?.Name ?? item.InstitutionName,
            Logo = info?.Logo,
            Website = info?.Url,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Institutions.Add(institution);
        await _db.SaveChangesAsync();
        return institution;
    }

    private async Task UpsertAccountAsync(PlaidAccountData pa, string institutionId)
    {
        var existing = await _db.Accounts.FirstOrDefaultAsync(a => a.PlaidAccountId == pa.AccountId);
        if (existing == null)
        {
            _db.Accounts.Add(new Account
            {
                Id = Guid.NewGuid().ToString(),
                PlaidAccountId = pa.AccountId,
                InstitutionId = institutionId,
                Name = pa.Name,
                Type = pa.Type,
                SubType = pa.SubType,
                Balance = pa.Balance,
                AvailableBalance = pa.AvailableBalance,
                Currency = pa.Currency,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Balance = pa.Balance;
            existing.AvailableBalance = pa.AvailableBalance;
            existing.UpdatedAt = DateTime.UtcNow;
        }
    }
}
