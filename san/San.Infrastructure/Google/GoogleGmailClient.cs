using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util;
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
        var request = flow.CreateAuthorizationCodeRequest(redirectUri);
        var uri = request.Build();
        // access_type=offline + prompt=consent — without both, Google only returns a
        // refresh_token on the FIRST-ever consent for that account; re-connecting a
        // previously-revoked account would silently come back with none.
        return uri.AbsoluteUri + "&access_type=offline&prompt=consent";
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
        listReq.MaxResults = 25;
        var list = await listReq.ExecuteAsync();

        var messages = new List<EmailMessage>();
        foreach (var m in list.Messages ?? [])
        {
            var getReq = service.Users.Messages.Get("me", m.Id);
            getReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getReq.MetadataHeaders = new Repeatable<string>(["From", "Subject"]);
            var full = await getReq.ExecuteAsync();

            var receivedAt = full.InternalDate is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : DateTime.UtcNow;
            if (receivedAt <= sinceUtc) continue;

            var headers = full.Payload?.Headers ?? [];
            var from = headers.FirstOrDefault(h => h.Name == "From")?.Value ?? "(unknown sender)";
            var subject = headers.FirstOrDefault(h => h.Name == "Subject")?.Value ?? "(no subject)";
            messages.Add(new EmailMessage(from, subject, full.Snippet ?? "", receivedAt));
        }

        // Refresh tokens rotate on some flows — always persist whatever credential.Token
        // holds now, not the token we started with.
        var updatedJson = JsonSerializer.Serialize(credential.Token);
        return (updatedJson, messages.OrderBy(m => m.ReceivedAtUtc).ToList());
    }

    private GoogleAuthorizationCodeFlow BuildFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
        Scopes = Scopes
    });
}
