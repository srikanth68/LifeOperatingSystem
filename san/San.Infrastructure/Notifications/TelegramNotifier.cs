using System.Text;
using System.Text.Json;
using San.Application.Interfaces;

namespace San.Infrastructure.Notifications;

public class TelegramNotifier(HttpClient http) : ITelegramNotifier
{
    private static string BotToken => Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";
    private static string ChatId => Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatId);

    public async Task SendAsync(string message, CancellationToken ct = default)
    {
        if (!IsConfigured) return; // silently no-op if not configured — caller already logs

        var url = $"https://api.telegram.org/bot{BotToken}/sendMessage";
        var payload = new { chat_id = ChatId, text = message };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();
    }
}
