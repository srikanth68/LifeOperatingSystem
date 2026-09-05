namespace Aasthi.Application.Interfaces;

// One bank transaction as Aasthi cares about it.
//
// A deliberately thin projection of Vault's Transaction. Aasthi has no business
// knowing about Plaid ids, pending flags or raw payloads, and copying the full shape
// would couple this module to a schema it does not own.
//
// Amount follows Vault's convention, which follows Plaid's: POSITIVE IS MONEY OUT.
// A $2,400 rent deposit arrives as -2400. Getting this backwards would let a mortgage
// payment satisfy a rent expectation, so nothing here compares raw amounts without
// first agreeing on direction.
public record VaultTransaction(
    string Id,
    decimal Amount,
    DateOnly Date,
    string Description,
    string? MerchantName)
{
    public bool IsMoneyOut => Amount > 0;
    public decimal Magnitude => Math.Abs(Amount);

    // Everything the match hint might be found in, lowercased once.
    public string Haystack => $"{Description} {MerchantName}".ToLowerInvariant();
}

public interface IVaultTransactions
{
    // Transactions in a date window. `q` is Vault's free-text filter over merchant and
    // description; passing it lets the bank do the narrowing instead of pulling a
    // month of rows across the network to discard most of them.
    Task<List<VaultTransaction>> GetAsync(
        DateOnly from, DateOnly to, string? q = null, int limit = 500, CancellationToken ct = default);
}
