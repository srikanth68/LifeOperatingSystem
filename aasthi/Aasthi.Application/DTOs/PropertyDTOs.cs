namespace Aasthi.Application.DTOs;

public record PropertyResult(
    Guid Id,
    string Address,
    string City,
    string State,
    string Zip,
    string Country,
    double? Latitude,
    double? Longitude,
    decimal PurchasePrice,
    DateOnly? PurchaseDate,
    decimal CurrentValue,
    DateOnly? CurrentValueAsOf,
    string Notes,
    DateTime CreatedAt,
    decimal ProfitAmount,
    double? ProfitPct,
    int ContactCount,
    int DocumentCount
);

public record PropertyDetailResult(
    Guid Id,
    string Address,
    string City,
    string State,
    string Zip,
    string Country,
    double? Latitude,
    double? Longitude,
    decimal PurchasePrice,
    DateOnly? PurchaseDate,
    decimal CurrentValue,
    DateOnly? CurrentValueAsOf,
    string Notes,
    DateTime CreatedAt,
    decimal ProfitAmount,
    double? ProfitPct,
    List<ContactResult> Contacts,
    List<DocumentResult> Documents
);

public record PropertyUpsertRequest(
    string Address,
    string City,
    string State,
    string Zip,
    string? Country,
    double? Latitude,
    double? Longitude,
    decimal PurchasePrice,
    DateOnly? PurchaseDate,
    decimal CurrentValue,
    DateOnly? CurrentValueAsOf,
    string? Notes
);

public record PortfolioSummary(
    int PropertyCount,
    decimal TotalPurchasePrice,
    decimal TotalCurrentValue,
    decimal TotalProfit,
    double? TotalProfitPct
);

public record ContactResult(
    Guid Id,
    Guid PropertyId,
    string Name,
    string Role,
    string Phone,
    string Email,
    string Notes
);

public record ContactUpsertRequest(
    string Name,
    string Role,
    string? Phone,
    string? Email,
    string? Notes
);

public record DocumentResult(
    Guid Id,
    Guid PropertyId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Category,
    DateTime UploadedAt
);

public record TaskResult(
    Guid Id,
    Guid PropertyId,
    string Title,
    string Description,
    DateOnly? DueDate,
    string Status,
    string Priority,
    string Source,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

public record TaskUpsertRequest(
    string Title,
    string? Description,
    DateOnly? DueDate,
    string? Priority,
    string? Source
);

public record TaskStatusUpdate(
    string Status
);

// ── Financials ──
public record FinancialEntryResult(
    Guid Id,
    Guid PropertyId,
    string Type,
    string Category,
    decimal Amount,
    DateOnly Date,
    string? Notes,
    DateTime CreatedAt
);

public record FinancialEntryRequest(
    string Type,
    string Category,
    decimal Amount,
    DateOnly Date,
    string? Notes
);

public record PropertyCashFlow(
    Guid PropertyId,
    string Address,
    decimal Income,
    decimal Expenses,
    decimal Mortgage,
    decimal NetCashFlow,
    decimal Appreciation,
    double? AppreciationPct,
    double? CashOnCashPct   // annualized cash flow / purchase price
);

public record FinancialsSummary(
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal TotalMortgage,
    decimal NetCashFlow,
    List<PropertyCashFlow> ByProperty
);

// ── Maintenance ──
public record MaintenanceResult(
    Guid Id,
    Guid PropertyId,
    string Title,
    string? Description,
    string? VendorName,
    string? VendorContact,
    decimal? Cost,
    string Category,
    DateOnly? CompletedDate,
    DateTime CreatedAt
);

public record MaintenanceRequest(
    string Title,
    string? Description,
    string? VendorName,
    string? VendorContact,
    decimal? Cost,
    string Category,
    DateOnly? CompletedDate
);

public record MaintenanceSummary(
    decimal TotalSpend,
    Dictionary<string, decimal> ByCategory,
    Dictionary<string, decimal> ByProperty,
    int LogCount
);
