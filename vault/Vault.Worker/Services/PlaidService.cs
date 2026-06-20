using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Vault.Worker.Data;
using Vault.Worker.Models;

namespace Vault.Worker.Services;

public interface IPlaidService
{
    Task<string> CreateLinkTokenAsync();
    Task<(string accessToken, string itemId)> ExchangePublicTokenAsync(string publicToken);
    Task<PlaidInstitutionInfo?> GetInstitutionInfoAsync(string plaidInstitutionId);
    Task<List<PlaidAccountData>> GetAccountsAsync(string accessToken);
    Task<List<PlaidTransactionData>> GetTransactionsAsync(string accessToken, DateTime startDate, DateTime endDate);
    Task<bool> ValidateCredentialsAsync();
}

public record PlaidInstitutionInfo(string InstitutionId, string Name, string? Logo, string? Url);
public record PlaidAccountData(string AccountId, string Name, string Type, string SubType, decimal Balance, decimal? AvailableBalance, string Currency);
public record PlaidTransactionData(string TransactionId, string AccountId, decimal Amount, string Currency, DateTime Date, string Name, string? MerchantName, string? Category);

public class PlaidService : IPlaidService
{
    private readonly HttpClient _http;
    private readonly ILogger<PlaidService> _logger;
    private readonly VaultDbContext _db;
    private readonly string _clientId;
    private readonly string _apiKey;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PlaidService(ILogger<PlaidService> logger, VaultDbContext db)
    {
        _logger = logger;
        _db = db;

        _clientId = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID") ?? "";
        _apiKey = Environment.GetEnvironmentVariable("PLAID_API_KEY") ?? "";
        var env = Environment.GetEnvironmentVariable("PLAID_ENV") ?? "sandbox";

        var baseUrl = env switch
        {
            "production" => "https://production.plaid.com",
            "development" => "https://development.plaid.com",
            _ => "https://sandbox.plaid.com"
        };

        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<bool> ValidateCredentialsAsync()
    {
        return !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_apiKey);
    }

    public async Task<string> CreateLinkTokenAsync()
    {
        var payload = new
        {
            client_id = _clientId,
            secret = _apiKey,
            client_name = "Vault Finance",
            country_codes = new[] { "US" },
            language = "en",
            user = new { client_user_id = "vault-user-1" },
            products = new[] { "transactions" }
        };

        var response = await PostAsync("/link/token/create", payload);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Plaid link/token/create failed: {Body}", body);
            throw new InvalidOperationException($"Failed to create link token: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("link_token").GetString()
            ?? throw new InvalidOperationException("No link_token in Plaid response");
    }

    public async Task<(string accessToken, string itemId)> ExchangePublicTokenAsync(string publicToken)
    {
        var payload = new
        {
            client_id = _clientId,
            secret = _apiKey,
            public_token = publicToken
        };

        var response = await PostAsync("/item/public_token/exchange", payload);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Plaid token exchange failed: {Body}", body);
            throw new InvalidOperationException($"Token exchange failed: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var itemId = doc.RootElement.GetProperty("item_id").GetString()!;
        return (accessToken, itemId);
    }

    public async Task<PlaidInstitutionInfo?> GetInstitutionInfoAsync(string plaidInstitutionId)
    {
        var payload = new
        {
            client_id = _clientId,
            secret = _apiKey,
            institution_id = plaidInstitutionId,
            country_codes = new[] { "US" },
            options = new { include_optional_metadata = true }
        };

        var response = await PostAsync("/institutions/get_by_id", payload);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var inst = doc.RootElement.GetProperty("institution");

        return new PlaidInstitutionInfo(
            inst.GetProperty("institution_id").GetString()!,
            inst.GetProperty("name").GetString()!,
            inst.TryGetProperty("logo", out var logo) ? logo.GetString() : null,
            inst.TryGetProperty("url", out var url) ? url.GetString() : null
        );
    }

    public async Task<List<PlaidAccountData>> GetAccountsAsync(string accessToken)
    {
        var payload = new { client_id = _clientId, secret = _apiKey, access_token = accessToken };
        var response = await PostAsync("/accounts/get", payload);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Plaid accounts/get failed: {Body}", body);
            return new();
        }

        using var doc = JsonDocument.Parse(body);
        var accounts = new List<PlaidAccountData>();

        foreach (var a in doc.RootElement.GetProperty("accounts").EnumerateArray())
        {
            var balances = a.GetProperty("balances");
            accounts.Add(new PlaidAccountData(
                a.GetProperty("account_id").GetString()!,
                a.GetProperty("name").GetString()!,
                a.GetProperty("type").GetString()!,
                a.TryGetProperty("subtype", out var st) ? st.GetString() ?? "" : "",
                balances.TryGetProperty("current", out var cur) && cur.ValueKind != JsonValueKind.Null
                    ? cur.GetDecimal() : 0,
                balances.TryGetProperty("available", out var avail) && avail.ValueKind != JsonValueKind.Null
                    ? avail.GetDecimal() : null,
                balances.TryGetProperty("iso_currency_code", out var cc) ? cc.GetString() ?? "USD" : "USD"
            ));
        }

        return accounts;
    }

    public async Task<List<PlaidTransactionData>> GetTransactionsAsync(string accessToken, DateTime startDate, DateTime endDate)
    {
        var allTransactions = new List<PlaidTransactionData>();
        int offset = 0;
        int total = int.MaxValue;

        while (offset < total)
        {
            var payload = new
            {
                client_id = _clientId,
                secret = _apiKey,
                access_token = accessToken,
                start_date = startDate.ToString("yyyy-MM-dd"),
                end_date = endDate.ToString("yyyy-MM-dd"),
                options = new { count = 500, offset }
            };

            var response = await PostAsync("/transactions/get", payload);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Plaid transactions/get failed: {Body}", body);
                break;
            }

            using var doc = JsonDocument.Parse(body);
            total = doc.RootElement.GetProperty("total_transactions").GetInt32();

            foreach (var t in doc.RootElement.GetProperty("transactions").EnumerateArray())
            {
                var categories = t.TryGetProperty("category", out var catArr) && catArr.ValueKind == JsonValueKind.Array
                    ? catArr.EnumerateArray().Select(c => c.GetString()).ToList()
                    : new List<string?>();

                allTransactions.Add(new PlaidTransactionData(
                    t.GetProperty("transaction_id").GetString()!,
                    t.GetProperty("account_id").GetString()!,
                    t.GetProperty("amount").GetDecimal(),
                    t.TryGetProperty("iso_currency_code", out var cc) ? cc.GetString() ?? "USD" : "USD",
                    DateTime.Parse(t.GetProperty("date").GetString()!),
                    t.GetProperty("name").GetString()!,
                    t.TryGetProperty("merchant_name", out var mn) && mn.ValueKind != JsonValueKind.Null
                        ? mn.GetString() : null,
                    categories.FirstOrDefault()
                ));

                offset++;
            }

            if (!doc.RootElement.GetProperty("transactions").EnumerateArray().Any()) break;
        }

        return allTransactions;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, _json);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(path, content);
    }
}
