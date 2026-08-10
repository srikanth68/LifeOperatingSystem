using Microsoft.EntityFrameworkCore;

namespace San.Infrastructure.Data;

// San has no EF migrations — startup calls EnsureCreated, which does nothing at all
// once the database file exists. So every table added after the first deployment,
// and every column added to one, has to be patched in by hand here.
//
// Called by BOTH San.API and San.Worker. They are separate containers sharing one
// SQLite file and there is no ordering between them; whichever wins does the work,
// and the loser finds everything already present. Previously only San.API patched,
// which left the worker to crash-loop on "no such column" until the API happened to
// come up first.
public static class SanSchema
{
    public static async Task EnsureAsync(SanDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS People (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL DEFAULT '',
                Phone TEXT,
                Email TEXT,
                Birthday TEXT,
                Relationship TEXT NOT NULL DEFAULT 'other',
                Notes TEXT,
                Tags TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE INDEX IF NOT EXISTS IX_People_Name ON People(Name);
            CREATE INDEX IF NOT EXISTS IX_People_Birthday ON People(Birthday);
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS EmailAccounts (
                Id TEXT PRIMARY KEY,
                Provider TEXT NOT NULL,
                EmailAddress TEXT NOT NULL,
                TokenJson TEXT NOT NULL DEFAULT '',
                Active INTEGER NOT NULL DEFAULT 1,
                LastCheckedAt TEXT,
                CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_EmailAccounts_Provider_Email ON EmailAccounts(Provider, EmailAddress);
            CREATE TABLE IF NOT EXISTS NotificationLedger (
                Key TEXT PRIMARY KEY,
                Severity TEXT NOT NULL DEFAULT 'medium',
                LastMessage TEXT NOT NULL DEFAULT '',
                Source TEXT NOT NULL DEFAULT '',
                NotifyCount INTEGER NOT NULL DEFAULT 0,
                FirstSeenAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                LastNotifiedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
                DueOn TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_NotificationLedger_LastNotifiedAt ON NotificationLedger(LastNotifiedAt);
            """, ct);

        // Columns added to tables that already exist in deployed databases. SQLite has
        // no ADD COLUMN IF NOT EXISTS, so each is checked against PRAGMA table_info.
        // Defaults matter: an existing ledger row gets KnowledgeMessage='' and
        // KnowledgeAt=default, which KnowledgePolicy reads as "the brain has never
        // heard this" and records once — the correct answer for rows written before
        // knowledge tracking existed.
        await AddColumnAsync(db, "NotificationLedger", "LastSeenAt", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'", ct);
        await AddColumnAsync(db, "NotificationLedger", "SeenCount", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnAsync(db, "NotificationLedger", "KnowledgeAt", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'", ct);
        await AddColumnAsync(db, "NotificationLedger", "KnowledgeMessage", "TEXT NOT NULL DEFAULT ''", ct);
    }

    private static async Task AddColumnAsync(
        SanDbContext db, string table, string column, string definition, CancellationToken ct)
    {
        if (await HasColumnAsync(db, table, column, ct)) return;
        // EF1002 suppressed deliberately: these are DDL identifiers, which SQLite
        // cannot parameterise at all, and all three values are compile-time literals
        // from the call sites above — none of it is reachable from user input.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition};", ct);
#pragma warning restore EF1002
    }

    private static async Task<bool> HasColumnAsync(
        SanDbContext db, string table, string column, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
