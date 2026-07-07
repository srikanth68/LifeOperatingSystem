using System.Text;
using System.Text.Json;
using Karma.Application.Interfaces;

namespace Karma.Infrastructure.Notifications;

public class NotificationSender(HttpClient http) : INotificationSender
{
    private static string BotToken => Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";
    private static string ChatId => Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatId);

    public async Task SendAsync(string message, string channel = "telegram", CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        // Currently only Telegram is supported; channel is reserved for future (email, push, etc.)
        var url = $"https://api.telegram.org/bot{BotToken}/sendMessage";
        var payload = new { chat_id = ChatId, text = message, parse_mode = "HTML" };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();
    }
}
