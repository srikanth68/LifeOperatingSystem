namespace San.Application.Interfaces;

public interface IGoogleCalendarService
{
    string GetAuthUrl();
    Task<bool> HandleCallbackAsync(string code);
    Task<int> SyncEventsAsync(CancellationToken ct);
    bool IsConfiguredAndAuthorized { get; }
}
