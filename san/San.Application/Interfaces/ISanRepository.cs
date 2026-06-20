using San.Domain.Entities;

namespace San.Application.Interfaces;

public interface ISanRepository
{
    // Chat
    Task<List<ChatMessage>> GetChatHistoryAsync(int take = 50);
    Task<ChatMessage> AddChatMessageAsync(ChatMessage message);

    // Reminders
    Task<List<Reminder>> GetRemindersAsync();
    Task<Reminder?> GetReminderAsync(Guid id);
    Task<Reminder> AddReminderAsync(Reminder reminder);
    Task<Reminder?> UpdateReminderAsync(Guid id, Action<Reminder> apply);
    Task<bool> DeleteReminderAsync(Guid id);
    Task<List<Reminder>> GetDueUnnotifiedRemindersAsync(DateTime asOf);

    // Alerts
    Task<List<Alert>> GetAlertsAsync();
    Task<Alert?> GetAlertAsync(Guid id);
    Task<Alert> AddAlertAsync(Alert alert);
    Task<Alert?> UpdateAlertAsync(Guid id, Action<Alert> apply);
    Task<bool> DeleteAlertAsync(Guid id);
    Task<List<Alert>> GetActiveAlertsAsync();
}
