namespace Vault.API.Models;

public class InstitutionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Logo { get; set; }
    public string? Website { get; set; }
}

public class AccountDto
{
    public string Id { get; set; } = string.Empty;
    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SubType { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal? AvailableBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; }
}

public class TransactionDto
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? MerchantName { get; set; }
    public string? Category { get; set; }
    public bool IsPending { get; set; }
}

// ─── Summary DTOs ───

public class DashboardSummaryDto
{
    public decimal NetWorth { get; set; }
    public decimal TotalCash { get; set; }
    public decimal TotalDebt { get; set; }
    public List<InstitutionBalanceDto> CashByInstitution { get; set; } = new();
    public List<InstitutionBalanceDto> DebtByInstitution { get; set; } = new();
    public List<CategorySpendingDto> SpendingByCategory { get; set; } = new();
    public List<TransactionDto> RecentTransactions { get; set; } = new();
    public List<UpcomingBillDto> UpcomingBills { get; set; } = new();
}

public class InstitutionBalanceDto
{
    public string InstitutionName { get; set; } = string.Empty;
    public decimal TotalBalance { get; set; }
    public List<AccountBalanceDto> Accounts { get; set; } = new();
}

public class AccountBalanceDto
{
    public string Name { get; set; } = string.Empty;
    public string SubType { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}

public class CategorySpendingDto
{
    public string Category { get; set; } = string.Empty;
    public double TotalAmount { get; set; }
    public int TransactionCount { get; set; }
}

public class UpcomingBillDto
{
    public string MerchantName { get; set; } = string.Empty;
    public decimal LastAmount { get; set; }
    public DateTime LastDate { get; set; }
    public DateTime EstimatedNextDate { get; set; }
}

public class SummaryDto
{
    public decimal TotalBalance { get; set; }
    public decimal TotalAvailableBalance { get; set; }
    public List<InstitutionSummaryDto> ByInstitution { get; set; } = new();
    public List<CategorySpendingDto> CategoriesLastMonth { get; set; } = new();
}

public class InstitutionSummaryDto
{
    public string InstitutionName { get; set; } = string.Empty;
    public decimal TotalBalance { get; set; }
    public int AccountCount { get; set; }
}

public class SyncStatusDto
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime LastSyncTime { get; set; }
    public int? TransactionsAdded { get; set; }
    public string? ErrorMessage { get; set; }
}

// ─── Category Group DTOs ───

public class CategoryGroupDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public string Color { get; set; } = "#c9a227";
    public string? Notes { get; set; }
    public List<CategoryGroupItemDto> Items { get; set; } = new();
}

public class CategoryGroupItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
    public bool IsIncome { get; set; }
    public string? Label { get; set; }
}

public class CategoryGroupSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public string Color { get; set; } = "#c9a227";
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal Net { get; set; }
    public List<TransactionDto> Transactions { get; set; } = new();
}

public record CreateCategoryGroupRequest(string Name, decimal Budget, string Color, string? Notes);
public record AddCategoryGroupItemRequest(string Keyword, bool IsIncome, string? Label);
