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

    // Calendar
    Task<List<CalendarEvent>> GetCalendarEventsAsync(DateTime from, DateTime to);
    Task<CalendarEvent> UpsertCalendarEventAsync(CalendarEvent ev);

    // Location
    Task<LocationUpdate?> GetLatestLocationAsync();
    Task<LocationUpdate> AddLocationUpdateAsync(LocationUpdate loc);

    // Activity Snapshots
    Task<ActivitySnapshot> AddActivitySnapshotAsync(ActivitySnapshot snap);
    Task<List<ActivitySnapshot>> GetRecentActivitySnapshotsAsync(int count);

    // People
    Task<List<Person>> GetPeopleAsync(string? query = null);
    Task<Person?> GetPersonAsync(Guid id);
    Task<Person> AddPersonAsync(Person person);
    Task<Person?> UpdatePersonAsync(Guid id, Action<Person> apply);
    Task<bool> DeletePersonAsync(Guid id);
    Task<List<Person>> GetUpcomingBirthdaysAsync(int withinDays = 30);

    // Settings (key-value)
    Task<string?> GetSettingAsync(string key);
    Task SetSettingAsync(string key, string value);
}
