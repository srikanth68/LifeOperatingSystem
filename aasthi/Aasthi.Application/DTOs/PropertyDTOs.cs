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
