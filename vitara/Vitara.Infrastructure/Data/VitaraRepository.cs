using Microsoft.EntityFrameworkCore;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.Infrastructure.Data;

public class VitaraRepository(VitaraDbContext db) : IVitaraRepository
{
    public async Task<OuraToken?> GetTokenAsync() =>
        await db.Tokens.OrderByDescending(t => t.LinkedAt).FirstOrDefaultAsync();

    public async Task SaveTokenAsync(OuraToken token)
    {
        var existing = await db.Tokens.FirstOrDefaultAsync();
        if (existing is null) db.Tokens.Add(token);
        else { existing.AccessToken = token.AccessToken; existing.RefreshToken = token.RefreshToken; existing.ExpiresAt = token.ExpiresAt; }
        await db.SaveChangesAsync();
    }

    public async Task DeleteTokenAsync()
    {
        db.Tokens.RemoveRange(db.Tokens);
        await db.SaveChangesAsync();
    }

    public async Task UpsertSleepAsync(IEnumerable<SleepSession> sessions)
    {
        foreach (var s in sessions)
        {
            var ex = await db.Sleep.FindAsync(s.Id);
            if (ex is null) db.Sleep.Add(s); else db.Entry(ex).CurrentValues.SetValues(s);
        }
        await db.SaveChangesAsync();
    }

    public Task<List<SleepSession>> GetSleepAsync(DateOnly from, DateOnly to) =>
        db.Sleep.Where(s => s.Day >= from && s.Day <= to).OrderBy(s => s.Day).ToListAsync();

    public async Task UpsertReadinessAsync(IEnumerable<DailyReadiness> records)
    {
        foreach (var r in records)
        {
            var ex = await db.Readiness.FindAsync(r.Id);
            if (ex is null) db.Readiness.Add(r); else db.Entry(ex).CurrentValues.SetValues(r);
        }
        await db.SaveChangesAsync();
    }

    public Task<List<DailyReadiness>> GetReadinessAsync(DateOnly from, DateOnly to) =>
        db.Readiness.Where(r => r.Day >= from && r.Day <= to).OrderBy(r => r.Day).ToListAsync();

    public async Task UpsertActivityAsync(IEnumerable<DailyActivity> records)
    {
        foreach (var r in records)
        {
            var ex = await db.Activity.FindAsync(r.Id);
            if (ex is null) db.Activity.Add(r); else db.Entry(ex).CurrentValues.SetValues(r);
        }
        await db.SaveChangesAsync();
    }

    public Task<List<DailyActivity>> GetActivityAsync(DateOnly from, DateOnly to) =>
        db.Activity.Where(a => a.Day >= from && a.Day <= to).OrderBy(a => a.Day).ToListAsync();
}
