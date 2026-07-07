namespace Karma.Application.Interfaces;

public interface INotificationSender
{
    bool IsConfigured { get; }
    Task SendAsync(string message, string channel = "telegram", CancellationToken ct = default);
}
