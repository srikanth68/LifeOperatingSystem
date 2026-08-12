using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using San.Application.Interfaces;
using San.Domain.Entities;

// Deliberately NOT "San.Infrastructure.Microsoft" — that would shadow the real
// Microsoft.* root namespace (Microsoft.Extensions.Hosting etc.) for any file that
// `using`s both this namespace and San.Infrastructure, causing ambiguous-reference
// build errors. "Outlook" avoids the collision and matches Google's own folder naming.
namespace San.Infrastructure.Outlook;

// Outlook/Office365 mail via Microsoft Graph, plain HTTP (no MSAL dependency — mirrors
// how Vitara's OuraClient handles OAuth by hand). "common" tenant so both personal
// Microsoft accounts and work/school accounts can connect.
public class MicrosoftGraphClient(HttpClient http) : IEmailProviderClient
{
    private const string AuthorizeUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
    private const string TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const string Scopes = "offline_access Mail.Read User.Read";

    public string Provider => "microsoft";

    private string? ClientId => Environment.GetEnvironmentVariable("MS_CLIENT_ID");
    private string? ClientSecret => Environment.GetEnvironmentVariable("MS_CLIENT_SECRET");

    public string BuildAuthUrl(string redirectUri)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = ClientId ?? "",
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = Scopes,
        };
        return AuthorizeUrl + "?" + string.Join("&", q.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<(string EmailAddress, string TokenJson)> ExchangeCodeAsync(string code, string redirectUri)
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = ClientId ?? "",
            ["client_secret"] = ClientSecret ?? "",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = Scopes,
        });

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{GraphBase}/me?$select=mail,userPrincipalName");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var me = await resp.Content.ReadFromJsonAsync<GraphUser>()
            ?? throw new InvalidOperationException("Empty /me response from Microsoft Graph");
        var email = me.Mail ?? me.UserPrincipalName
            ?? throw new InvalidOperationException("Microsoft account has neither mail nor userPrincipalName.");

        return (email, JsonSerializer.Serialize(token));
    }

    public async Task<(string UpdatedTokenJson, List<EmailMessage> Messages)> FetchNewMessagesAsync(EmailAccount account, DateTime sinceUtc)
    {
        var token = JsonSerializer.Deserialize<MsTokenResponse>(account.TokenJson)
            ?? throw new InvalidOperationException($"EmailAccount {account.Id} has no usable token.");
        var storedRefreshToken = token.RefreshToken
            ?? throw new InvalidOperationException("No refresh_token stored — reconnect this account.");

        // Access tokens are short-lived (~1h) — always refresh; cheap and avoids a
        // separate expiry-tracking column. Microsoft doesn't always return a new
        // refresh_token in the response — fall back to the one we sent so we don't
        // overwrite a good stored token with null and brick future refreshes.
        token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = ClientId ?? "",
            ["client_secret"] = ClientSecret ?? "",
            ["refresh_token"] = storedRefreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = Scopes,
        });
        token = token with { RefreshToken = token.RefreshToken ?? storedRefreshToken };

        var filter = $"receivedDateTime ge {sinceUtc:yyyy-MM-ddTHH:mm:ssZ}";
        // internetMessageHeaders carries List-Unsubscribe, List-Id and Precedence —
        // the same signals EmailFilter uses to drop bulk mail before the model reads
        // it. Graph returns them only when explicitly selected, and documents that a
        // collection query may omit them anyway; when that happens the filter simply
        // sees no headers and keeps the message, which is the safe direction to fail.
        var url = $"{GraphBase}/me/mailFolders/inbox/messages" +
                  $"?$filter={Uri.EscapeDataString(filter)}" +
                  $"&$select=from,subject,bodyPreview,receivedDateTime,internetMessageHeaders" +
                  $"&$top=25&$orderby=receivedDateTime asc";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var page = await resp.Content.ReadFromJsonAsync<GraphMessagePage>()
            ?? new GraphMessagePage();

        var messages = (page.Value ?? [])
            .Select(m => new EmailMessage(
                m.From?.EmailAddress?.Address ?? "(unknown sender)",
                m.Subject ?? "(no subject)",
                m.BodyPreview ?? "",
                m.ReceivedDateTime,
                // Lower-cased keys, matching the Gmail client — header names are
                // case-insensitive per RFC 5322 and the filter looks them up that way.
                // Duplicates (a message can carry several Received headers) keep the
                // first; the filter only ever tests for presence or a simple value.
                (m.InternetMessageHeaders ?? [])
                    .Where(h => !string.IsNullOrEmpty(h.Name))
                    .GroupBy(h => h.Name!.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First().Value ?? ""),
                // Graph has no equivalent of Gmail's CATEGORY_* classification, so
                // Outlook relies on headers alone.
                []))
            .ToList();

        return (JsonSerializer.Serialize(token), messages);
    }

    private async Task<MsTokenResponse> PostTokenAsync(Dictionary<string, string> form)
    {
        var resp = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form));
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Microsoft token request failed: HTTP {(int)resp.StatusCode} — {body}");
        return JsonSerializer.Deserialize<MsTokenResponse>(body)
            ?? throw new InvalidOperationException("Empty token response from Microsoft.");
    }

    private record GraphUser(string? Mail, string? UserPrincipalName);
    private record GraphMessagePage(List<GraphMessage>? Value = null);
    private record GraphMessage(
        GraphFrom? From, string? Subject, string? BodyPreview, DateTime ReceivedDateTime,
        // Null whenever Graph declines to expand it on a collection query — handled at
        // the call site rather than defaulted, so "absent" stays distinguishable.
        List<GraphHeader>? InternetMessageHeaders);
    private record GraphFrom(GraphEmailAddress? EmailAddress);
    private record GraphEmailAddress(string? Address);
    private record GraphHeader(string? Name, string? Value);

    private record MsTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    }
}
