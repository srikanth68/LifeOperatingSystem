using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aasthi.Application.Interfaces;
using Maaya.Auth;
using Microsoft.Extensions.Logging;

namespace Aasthi.Infrastructure.ModuleClients;

// Reads bank transactions out of Vault.
//
// Vault enforces Maaya's global JWT policy like every other module, so these calls
// carry a Bearer token Aasthi mints for itself from the shared TokenService and the
// shared JWT_SECRET -- the same arrangement San uses to reach its siblings.
//
// Failure is deliberately soft. Vault being unreachable should leave the ledger
// showing what it already knows rather than throwing a page at the user: an
// unreconciled month is a nuisance, an Aasthi that will not load because a sibling is
// down is worse. The caller sees an empty list and the log says why.
public class VaultTransactionClient(
    IHttpClientFactory httpFactory, TokenService tokens, ILogger<VaultTransactionClient> logger)
    : IVaultTransactions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<VaultTransaction>> GetAsync(
        DateOnly from, DateOnly to, string? q = null, int limit = 500, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/transactions?startDate={from:yyyy-MM-dd}&endDate={to:yyyy-MM-dd}&limit={limit}";
            if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";

            var http = httpFactory.CreateClient("vault");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", tokens.GenerateAccessToken("aasthi-service", "aasthi"));

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Vault returned HTTP {Status} for transactions {From}..{To}.",
                    (int)resp.StatusCode, from, to);
                return [];
            }

            var dtos = await resp.Content.ReadFromJsonAsync<List<VaultTransactionDto>>(Json, ct) ?? [];

            return dtos
                // Pending transactions still change: the amount can move and the row can
                // vanish. Matching a rent expectation against one would record money that
                // never arrived, and it costs nothing to wait -- it settles in a day and
                // gets matched on the next pass.
                .Where(d => !d.IsPending)
                .Select(d => new VaultTransaction(
                    d.Id, d.Amount, DateOnly.FromDateTime(d.TransactionDate), d.Description, d.MerchantName))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not reach Vault for transactions {From}..{To}.", from, to);
            return [];
        }
    }

    private sealed record VaultTransactionDto(
        string Id, decimal Amount, DateTime TransactionDate,
        string Description, string? MerchantName, bool IsPending);
}
