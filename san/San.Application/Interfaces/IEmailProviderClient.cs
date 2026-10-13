using San.Domain.Entities;

namespace San.Application.Interfaces;

// Headers and Labels are optional so a provider that cannot supply them still works —
// EmailFilter treats their absence as "no bulk signal", i.e. keep, which is the safe
// direction to fail in. Header keys are LOWERCASE by convention; the filter looks them
// up that way.
//
// Id and ThreadId are the provider's own: they make each email processed exactly once
// (ProcessedEmails) and let NorthStar find the earlier messages in a conversation.
// Body is the full text (BodyIsHtml when the provider only had HTML); EmailText turns
// it into what the model reads.
public record EmailMessage(
    string From,
    string Subject,
    string Snippet,
    DateTime ReceivedAtUtc,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyList<string>? Labels = null,
    string? Id = null,
    string? ThreadId = null,
    string? Body = null,
    bool BodyIsHtml = false);

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
