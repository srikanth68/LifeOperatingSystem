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

    public async Task ClearChatHistoryAsync()
    {
        db.ChatMessages.RemoveRange(db.ChatMessages);
        await db.SaveChangesAsync();
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

    // ── Calendar ──
    public Task<List<CalendarEvent>> GetCalendarEventsAsync(DateTime from, DateTime to) =>
        db.CalendarEvents.Where(e => e.StartTime >= from && e.StartTime <= to)
            .OrderBy(e => e.StartTime).ToListAsync();

    public async Task<CalendarEvent> UpsertCalendarEventAsync(CalendarEvent ev)
    {
        CalendarEvent? existing = null;
        if (ev.ExternalId is not null)
        {
            existing = await db.CalendarEvents
                .FirstOrDefaultAsync(e => e.ExternalId == ev.ExternalId && e.Source == ev.Source);
        }

        if (existing is not null)
        {
            existing.Title = ev.Title;
            existing.Description = ev.Description;
            existing.StartTime = ev.StartTime;
            existing.EndTime = ev.EndTime;
            existing.Location = ev.Location;
            existing.CalendarName = ev.CalendarName;
            existing.AllDay = ev.AllDay;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return existing;
        }

        db.CalendarEvents.Add(ev);
        await db.SaveChangesAsync();
        return ev;
    }

    // ── Location ──
    public Task<LocationUpdate?> GetLatestLocationAsync() =>
        db.LocationUpdates.OrderByDescending(l => l.Timestamp).FirstOrDefaultAsync();

    public async Task<LocationUpdate> AddLocationUpdateAsync(LocationUpdate loc)
    {
        db.LocationUpdates.Add(loc);
        await db.SaveChangesAsync();
        return loc;
    }

    // ── Activity Snapshots ──
    public async Task<ActivitySnapshot> AddActivitySnapshotAsync(ActivitySnapshot snap)
    {
        db.ActivitySnapshots.Add(snap);
        await db.SaveChangesAsync();
        return snap;
    }

    public Task<List<ActivitySnapshot>> GetRecentActivitySnapshotsAsync(int count) =>
        db.ActivitySnapshots.OrderByDescending(s => s.Timestamp).Take(count).ToListAsync();

    // ── People ──
    public async Task<List<Person>> GetPeopleAsync(string? query = null)
    {
        var q = db.People.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var lower = query.ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(lower)
                          || (p.Tags != null && p.Tags.ToLower().Contains(lower))
                          || (p.Notes != null && p.Notes.ToLower().Contains(lower)));
        }
        return await q.OrderBy(p => p.Name).ToListAsync();
    }

    public Task<Person?> GetPersonAsync(Guid id) => db.People.FindAsync(id).AsTask();

    public async Task<Person> AddPersonAsync(Person person)
    {
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    public async Task<Person?> UpdatePersonAsync(Guid id, Action<Person> apply)
    {
        var p = await db.People.FindAsync(id);
        if (p is null) return null;
        apply(p);
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return p;
    }

    public async Task<bool> DeletePersonAsync(Guid id)
    {
        var p = await db.People.FindAsync(id);
        if (p is null) return false;
        db.People.Remove(p);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<Person>> GetUpcomingBirthdaysAsync(int withinDays = 30)
    {
        var people = await db.People.Where(p => p.Birthday != null).ToListAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return people.Where(p =>
        {
            if (!DateOnly.TryParse(p.Birthday, out var bday)) return false;
            var thisYear = new DateOnly(today.Year, bday.Month, bday.Day);
            if (thisYear < today) thisYear = thisYear.AddYears(1);
            return thisYear.DayNumber - today.DayNumber <= withinDays;
        }).OrderBy(p =>
        {
            DateOnly.TryParse(p.Birthday, out var bday);
            var thisYear = new DateOnly(today.Year, bday.Month, bday.Day);
            if (thisYear < today) thisYear = thisYear.AddYears(1);
            return thisYear.DayNumber;
        }).ToList();
    }

    // ── Settings ──

    public async Task<string?> GetSettingAsync(string key) =>
        (await db.Settings.FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task SetSettingAsync(string key, string value)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        else
            existing.Value = value;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetSettingsByPrefixAsync(string prefix) =>
        (await db.Settings.Where(s => s.Key.StartsWith(prefix)).ToListAsync())
            .Select(s => new KeyValuePair<string, string>(s.Key, s.Value))
            .ToList();

    // ── Notification ledger ──
    // By last SEEN, not last notified: a row can now sit unnotified for days while
    // still being observed every 15 minutes, and those are exactly the rows worth
    // looking at first when checking whether suppression is behaving.
    public Task<List<NotificationLedgerEntry>> GetLedgerAsync() =>
        db.NotificationLedger.OrderByDescending(e => e.LastSeenAt).ToListAsync();

    public Task<NotificationLedgerEntry?> GetLedgerEntryAsync(string key) =>
        db.NotificationLedger.FirstOrDefaultAsync(e => e.Key == key);

    // One write per sighting. `notified` and `recordedKnowledge` are independent:
    // a finding can be seen and stay silent, be seen and told to the user, or be
    // seen, kept quiet, and still handed to NorthStar because it changed. Rolling
    // them together is what previously left the brain stuck on a finding's first
    // wording for the whole cooldown.
    public async Task RecordSightingAsync(NotificationLedgerEntry entry, bool notified, bool recordedKnowledge)
    {
        var existing = await db.NotificationLedger.FirstOrDefaultAsync(e => e.Key == entry.Key);
        if (existing is null)
        {
            entry.SeenCount = 1;
            entry.NotifyCount = notified ? 1 : 0;
            // A row that has never notified must not look like it just did, or the
            // cooldown would start running against a message nobody received.
            if (!notified) entry.LastNotifiedAt = DateTime.MinValue;
            if (!recordedKnowledge) { entry.KnowledgeAt = default; entry.KnowledgeMessage = ""; }
            db.NotificationLedger.Add(entry);
        }
        else
        {
            existing.Severity = entry.Severity;
            existing.LastMessage = entry.LastMessage;
            existing.Source = entry.Source;
            existing.DueOn = entry.DueOn;
            existing.LastSeenAt = entry.LastSeenAt;
            existing.SeenCount += 1;

            if (notified)
            {
                existing.NotifyCount += 1;
                existing.LastNotifiedAt = entry.LastNotifiedAt;
            }
            if (recordedKnowledge)
            {
                existing.KnowledgeAt = entry.KnowledgeAt;
                existing.KnowledgeMessage = entry.KnowledgeMessage;
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task ClearLedgerAsync()
    {
        db.NotificationLedger.RemoveRange(db.NotificationLedger);
        await db.SaveChangesAsync();
    }

    // ── Email accounts ──
    public Task<List<EmailAccount>> GetEmailAccountsAsync() =>
        db.EmailAccounts.OrderBy(a => a.EmailAddress).ToListAsync();

    public Task<EmailAccount?> GetEmailAccountAsync(Guid id) =>
        db.EmailAccounts.FirstOrDefaultAsync(a => a.Id == id);

    // Keyed by (Provider, EmailAddress) — reconnecting an already-connected mailbox
    // (e.g. re-authorizing after a revoked token) refreshes its tokens in place
    // rather than creating a duplicate row.
    public async Task<EmailAccount> UpsertEmailAccountAsync(string provider, string emailAddress, string tokenJson)
    {
        var existing = await db.EmailAccounts
            .FirstOrDefaultAsync(a => a.Provider == provider && a.EmailAddress == emailAddress);
        if (existing is not null)
        {
            existing.TokenJson = tokenJson;
            existing.Active = true;
            await db.SaveChangesAsync();
            return existing;
        }

        var account = new EmailAccount { Provider = provider, EmailAddress = emailAddress, TokenJson = tokenJson };
        db.EmailAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task<EmailAccount?> UpdateEmailAccountAsync(Guid id, Action<EmailAccount> apply)
    {
        var a = await db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return null;
        apply(a);
        await db.SaveChangesAsync();
        return a;
    }

    public async Task<bool> DeleteEmailAccountAsync(Guid id)
    {
        var a = await db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return false;
        db.EmailAccounts.Remove(a);
        await db.SaveChangesAsync();
        return true;
    }
}
