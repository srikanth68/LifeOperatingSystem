namespace San.Application.Interfaces;

public interface ITelegramNotifier
{
    bool IsConfigured { get; }
    Task SendAsync(string message, CancellationToken ct = default);
}
