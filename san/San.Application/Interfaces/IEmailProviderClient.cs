using San.Domain.Entities;

namespace San.Application.Interfaces;

public record EmailMessage(string From, string Subject, string Snippet, DateTime ReceivedAtUtc);

// One implementation per provider (google, microsoft). San.API resolves the right one by
// Provider key when starting/completing an OAuth connection; San.Worker's EmailTriageWorker
// resolves by the same key when polling an already-connected EmailAccount.
public interface IEmailProviderClient
{
    string Provider { get; }

    string BuildAuthUrl(string redirectUri);

    // Exchanges an OAuth authorization code for tokens and returns the account's real
    // address (read back from the provider, not trusted from the client) plus the
    // token set to persist.
    Task<(string EmailAddress, string TokenJson)> ExchangeCodeAsync(string code, string redirectUri);

    // Fetches messages received after sinceUtc, refreshing the access token first if
    // needed. Returns the (possibly updated) token JSON to persist alongside the messages
    // — refresh tokens rotate on some providers, so the caller must always re-save this.
    Task<(string UpdatedTokenJson, List<EmailMessage> Messages)> FetchNewMessagesAsync(EmailAccount account, DateTime sinceUtc);
}
