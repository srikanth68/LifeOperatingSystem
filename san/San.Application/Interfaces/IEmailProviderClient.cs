using San.Domain.Entities;

namespace San.Application.Interfaces;

// Headers and Labels are optional so a provider that cannot supply them still works —
// EmailFilter treats their absence as "no bulk signal", i.e. keep, which is the safe
// direction to fail in. Header keys are LOWERCASE by convention; the filter looks them
// up that way.
public record EmailMessage(
    string From,
    string Subject,
    string Snippet,
    DateTime ReceivedAtUtc,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyList<string>? Labels = null);

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
