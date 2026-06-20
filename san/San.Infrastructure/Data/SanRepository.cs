using Microsoft.EntityFrameworkCore;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.Infrastructure.Data;

public class SanRepository(SanDbContext db) : ISanRepository
{
    // ── Chat ──
    public async Task<List<ChatMessage>> GetChatHistoryAsync(int take = 50) =>
        await db.ChatMessages.OrderByDescending(m => m.CreatedAt).Take(take)
            .OrderBy(m => m.CreatedAt).ToListAsync();

    public async Task<ChatMessage> AddChatMessageAsync(ChatMessage message)
    {
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    // ── Reminders ──
    public async Task<List<Reminder>> GetRemindersAsync() =>
        await db.Reminders.OrderBy(r => r.Done).ThenBy(r => r.DueAt).ToListAsync();

    public Task<Reminder?> GetReminderAsync(Guid id) =>
        db.Reminders.FirstOrDefaultAsync(r => r.Id == id);

    public async Task<Reminder> AddReminderAsync(Reminder reminder)
    {
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync();
        return reminder;
    }

    public async Task<Reminder?> UpdateReminderAsync(Guid id, Action<Reminder> apply)
    {
        var r = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return null;
        apply(r);
        await db.SaveChangesAsync();
        return r;
    }

    public async Task<bool> DeleteReminderAsync(Guid id)
    {
        var r = await db.Reminders.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return false;
        db.Reminders.Remove(r);
        await db.SaveChangesAsync();
        return true;
    }

    public Task<List<Reminder>> GetDueUnnotifiedRemindersAsync(DateTime asOf) =>
        db.Reminders.Where(r => !r.Done && r.NotifyTelegram && r.NotifiedAt == null && r.DueAt <= asOf).ToListAsync();

    // ── Alerts ──
    public async Task<List<Alert>> GetAlertsAsync() =>
        await db.Alerts.OrderByDescending(a => a.CreatedAt).ToListAsync();

    public Task<Alert?> GetAlertAsync(Guid id) =>
        db.Alerts.FirstOrDefaultAsync(a => a.Id == id);

    public async Task<Alert> AddAlertAsync(Alert alert)
    {
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return alert;
    }

    public async Task<Alert?> UpdateAlertAsync(Guid id, Action<Alert> apply)
    {
        var a = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return null;
        apply(a);
        await db.SaveChangesAsync();
        return a;
    }

    public async Task<bool> DeleteAlertAsync(Guid id)
    {
        var a = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return false;
        db.Alerts.Remove(a);
        await db.SaveChangesAsync();
        return true;
    }

    public Task<List<Alert>> GetActiveAlertsAsync() =>
        db.Alerts.Where(a => a.Active).ToListAsync();
}
