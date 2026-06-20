using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.API.Models;
using Vault.Worker.Data;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SummaryController : ControllerBase
{
    private readonly VaultDbContext _db;

    public SummaryController(VaultDbContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboard()
    {
        var accounts = await _db.Accounts
            .Include(a => a.Institution)
            .Where(a => a.IsActive)
            .ToListAsync();

        var cashAccounts = accounts.Where(a =>
            a.Type.Equals("depository", StringComparison.OrdinalIgnoreCase)).ToList();
        var debtAccounts = accounts.Where(a =>
            a.Type.Equals("credit", StringComparison.OrdinalIgnoreCase) ||
            a.Type.Equals("loan", StringComparison.OrdinalIgnoreCase)).ToList();

        var totalCash = cashAccounts.Sum(a => a.Balance);
        var totalDebt = debtAccounts.Sum(a => Math.Abs(a.Balance));

        // Cash by institution (with nested accounts)
        var cashByInstitution = cashAccounts
            .GroupBy(a => a.Institution?.Name ?? "Unknown")
            .Select(g => new InstitutionBalanceDto
            {
                InstitutionName = g.Key,
                TotalBalance = g.Sum(a => a.Balance),
                Accounts = g.Select(a => new AccountBalanceDto
                {
                    Name = a.Name,
                    SubType = a.SubType,
                    Balance = a.Balance
                }).OrderByDescending(a => a.Balance).ToList()
            })
            .OrderByDescending(i => i.TotalBalance)
            .ToList();

        // Debt by institution (with nested accounts)
        var debtByInstitution = debtAccounts
            .GroupBy(a => a.Institution?.Name ?? "Unknown")
            .Select(g => new InstitutionBalanceDto
            {
                InstitutionName = g.Key,
                TotalBalance = g.Sum(a => Math.Abs(a.Balance)),
                Accounts = g.Select(a => new AccountBalanceDto
                {
                    Name = a.Name,
                    SubType = a.SubType,
                    Balance = Math.Abs(a.Balance)
                }).OrderByDescending(a => a.Balance).ToList()
            })
            .OrderByDescending(i => i.TotalBalance)
            .ToList();

        // Spending by category (last 30 days, positive amounts = expenses in Plaid)
        var since30 = DateTime.UtcNow.AddDays(-30);
        var recentTxns = await _db.Transactions
            .Include(t => t.Account).ThenInclude(a => a!.Institution)
            .Where(t => t.TransactionDate >= since30 && !t.IsPending)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();

        var spendingByCategory = recentTxns
            .Where(t => t.Amount > 0)
            .GroupBy(t => t.Category ?? "Other")
            .Select(g => new CategorySpendingDto
            {
                Category = g.Key,
                TotalAmount = (double)g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderByDescending(c => c.TotalAmount)
            .ToList();

        // Recent transactions (last 10)
        var recentDtos = recentTxns.Take(10).Select(t => new TransactionDto
        {
            Id = t.Id,
            AccountId = t.AccountId,
            AccountName = t.Account?.Name ?? "",
            InstitutionName = t.Account?.Institution?.Name ?? "",
            Amount = t.Amount,
            Currency = t.Currency,
            TransactionDate = t.TransactionDate,
            Description = t.Description,
            MerchantName = t.MerchantName,
            Category = t.Category,
            IsPending = t.IsPending
        }).ToList();

        // Upcoming bills: merchants appearing in both months -1 and -2
        var upcomingBills = await GetUpcomingBillsAsync();

        return Ok(new DashboardSummaryDto
        {
            NetWorth = totalCash - totalDebt,
            TotalCash = totalCash,
            TotalDebt = totalDebt,
            CashByInstitution = cashByInstitution,
            DebtByInstitution = debtByInstitution,
            SpendingByCategory = spendingByCategory,
            RecentTransactions = recentDtos,
            UpcomingBills = upcomingBills
        });
    }

    private async Task<List<UpcomingBillDto>> GetUpcomingBillsAsync()
    {
        var twoMonthsAgo = DateTime.UtcNow.AddMonths(-2);
        var txns = await _db.Transactions
            .Where(t => t.TransactionDate >= twoMonthsAgo && t.Amount > 0 && !t.IsPending && t.MerchantName != null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var lastMonthStart = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
        var prevMonthStart = lastMonthStart.AddMonths(-1);

        var bills = txns
            .Where(t => t.MerchantName != null)
            .GroupBy(t => t.MerchantName!)
            .Where(g =>
            {
                var inLastMonth = g.Any(t => t.TransactionDate >= lastMonthStart);
                var inPrevMonth = g.Any(t => t.TransactionDate >= prevMonthStart && t.TransactionDate < lastMonthStart);
                return inLastMonth && inPrevMonth;
            })
            .Select(g =>
            {
                var last = g.OrderByDescending(t => t.TransactionDate).First();
                return new UpcomingBillDto
                {
                    MerchantName = g.Key,
                    LastAmount = last.Amount,
                    LastDate = last.TransactionDate,
                    EstimatedNextDate = last.TransactionDate.AddMonths(1)
                };
            })
            .Where(b => b.EstimatedNextDate >= now)
            .OrderBy(b => b.EstimatedNextDate)
            .Take(5)
            .ToList();

        return bills;
    }

    [HttpGet("monthly-spending")]
    public async Task<ActionResult<List<object>>> GetMonthlySpendings([FromQuery] int months = 12)
    {
        var startDate = DateTime.UtcNow.AddMonths(-months);
        var transactions = await _db.Transactions
            .Where(t => t.TransactionDate >= startDate && t.Amount > 0 && !t.IsPending)
            .ToListAsync();

        var data = transactions
            .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month })
            .Select(g => new
            {
                Month = new DateTime(g.Key.Year, g.Key.Month, 1),
                TotalSpending = (double)g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderBy(x => x.Month)
            .ToList();

        return Ok(data);
    }
}
