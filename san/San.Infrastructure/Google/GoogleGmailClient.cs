using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using System.Text.Json;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.Infrastructure.Google;

// Multi-account Gmail reader for the email-triage worker. Deliberately separate from
// GoogleCalendarService — that one manages a single fixed calendar via a flat token
// file; this one can hold any number of Gmail accounts, keyed by address, tokens
// persisted in EmailAccounts.TokenJson (one DB row per connected mailbox).
public class GoogleGmailClient : IEmailProviderClient
{
    private static readonly string[] Scopes = [GmailService.Scope.GmailReadonly];

    public string Provider => "google";

    private string? ClientId => Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
    private string? ClientSecret => Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");

    public string BuildAuthUrl(string redirectUri)
    {
        var flow = BuildFlow();
        var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(redirectUri);
        // access_type=offline is already the SDK's default (appending it again as a raw
        // string caused Google's "parameters can only have a single value" 400) — only
        // Prompt needs setting explicitly: without it, Google only returns a refresh_token
        // on the FIRST-ever consent for that account; re-connecting a previously-revoked
        // account would silently come back with none.
        request.Prompt = "consent";
        return request.Build().AbsoluteUri;
    }

    public async Task<(string EmailAddress, string TokenJson)> ExchangeCodeAsync(string code, string redirectUri)
    {
        var flow = BuildFlow();
        var token = await flow.ExchangeCodeForTokenAsync("pending", code, redirectUri, CancellationToken.None);

        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = new UserCredential(flow, "pending", token),
            ApplicationName = "Maaya San"
        });
        var profile = await service.Users.GetProfile("me").ExecuteAsync();

        return (profile.EmailAddress, JsonSerializer.Serialize(token));
    }

    public async Task<(string UpdatedTokenJson, List<EmailMessage> Messages)> FetchNewMessagesAsync(EmailAccount account, DateTime sinceUtc)
    {
        var flow = BuildFlow();
        var token = JsonSerializer.Deserialize<TokenResponse>(account.TokenJson)
            ?? throw new InvalidOperationException($"EmailAccount {account.Id} has no usable token.");
        var credential = new UserCredential(flow, account.EmailAddress, token);

        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Maaya San"
        });

        // Gmail search query — 'after:' is date-granularity (not time-of-day), so we
        // over-fetch by a day and filter precisely on InternalDate below.
        var query = $"in:inbox after:{sinceUtc.AddDays(-1):yyyy/MM/dd}";
        var listReq = service.Users.Messages.List("me");
        listReq.Q = query;
        // Gmail lists newest first, so a cap smaller than a busy day drops the oldest
        // new mail. Already-processed messages are skipped by id, so over-fetching
        // costs a read, not a second triage.
        listReq.MaxResults = 50;
        var list = await listReq.ExecuteAsync();

        var messages = new List<EmailMessage>();
        foreach (var m in list.Messages ?? [])
        {
            var getReq = service.Users.Messages.Get("me", m.Id);
            // Full, not metadata: the body is read now rather than its first 200
            // characters, and the full payload carries every header the bulk filter
            // looks for (List-Unsubscribe, Precedence, ...) as well.
            getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            var full = await getReq.ExecuteAsync();

            var receivedAt = full.InternalDate is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : DateTime.UtcNow;
            if (receivedAt <= sinceUtc) continue;

            var headers = full.Payload?.Headers ?? [];
            var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "(unknown sender)";
            var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(no subject)";

            // Lower-cased keys: header names are case-insensitive per RFC 5322, and the
            // filter should not have to guess how a given sender capitalised them.
            var headerMap = headers
                .Where(h => !string.IsNullOrEmpty(h.Name))
                .GroupBy(h => h.Name!.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First().Value ?? "");

            var (body, isHtml) = BodyOf(full.Payload);
            messages.Add(new EmailMessage(
                from, subject, full.Snippet ?? "", receivedAt,
                headerMap,
                // Gmail's own CATEGORY_* classification, which is better than anything
                // worth rebuilding here.
                full.LabelIds?.ToList() ?? [],
                Id: full.Id, ThreadId: full.ThreadId, Body: body, BodyIsHtml: isHtml));
        }

        // Refresh tokens rotate on some flows — always persist whatever credential.Token
        // holds now, not the token we started with.
        var updatedJson = JsonSerializer.Serialize(credential.Token);
        return (updatedJson, messages.OrderBy(m => m.ReceivedAtUtc).ToList());
    }

    // The text/plain part when there is one, else the HTML part, searched depth first
    // through multipart/alternative and multipart/mixed. Attachments are skipped:
    // they have a filename, and their bytes are not in the payload anyway.
    private static (string? Body, bool IsHtml) BodyOf(global::Google.Apis.Gmail.v1.Data.MessagePart? part)
    {
        string? html = null;
        string? plain = null;
        void Walk(global::Google.Apis.Gmail.v1.Data.MessagePart? p)
        {
            if (p is null || plain is not null) return;
            if (string.IsNullOrEmpty(p.Filename) && p.Body?.Data is { Length: > 0 } data)
            {
                if (p.MimeType == "text/plain") plain = Decode(data);
                else if (p.MimeType == "text/html") html ??= Decode(data);
            }
            foreach (var child in p.Parts ?? []) Walk(child);
        }
        Walk(part);
        return plain is not null ? (plain, false) : (html, html is not null);
    }

    private static string Decode(string base64Url)
    {
        var s = base64Url.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch (FormatException) { return ""; }
    }

    private GoogleAuthorizationCodeFlow BuildFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
        Scopes = Scopes
    });
}
