using Microsoft.EntityFrameworkCore;
using Vault.Worker.Data;
using Vault.Worker.Models;

namespace Vault.Worker.Services;

public interface ISyncService
{
    Task<SyncMetadata> SyncAsync();
}

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

                    // Sync transactions
                    var plaidTxns = await _plaidService.GetTransactionsAsync(item.AccessToken, startDate, endDate);
                    foreach (var pt in plaidTxns)
                    {
                        var accountId = await _db.Accounts
                            .Where(a => a.PlaidAccountId == pt.AccountId)
                            .Select(a => a.Id)
                            .FirstOrDefaultAsync();

                        if (accountId == null) continue;

                        var exists = await _db.Transactions.AnyAsync(t => t.PlaidTransactionId == pt.TransactionId);
                        if (!exists)
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
                                IsPending = false,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            });
                            transactionsAdded++;
                        }
                    }

                    await _db.SaveChangesAsync();
                    _logger.LogInformation("Synced item {ItemId}: {Accounts} accounts, {Txns} transactions",
                        item.PlaidItemId, plaidAccounts.Count, plaidTxns.Count);
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
