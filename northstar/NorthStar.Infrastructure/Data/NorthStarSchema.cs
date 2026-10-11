using System.Data;
using System.Data.Common;

namespace NorthStar.Infrastructure.Data;

// Brings an existing northstar.db up to the current model. EnsureCreated builds a fresh
// database from the model and does nothing at all to one that exists, so every column
// and table added after the first deploy has to be added here too, or the live brain
// fails on its first query that names it.
//
// Additive only and idempotent: it runs on every start, adds what is missing, and never
// drops or rewrites anything that is there.
public static class NorthStarSchema
{
    private const string TablesSql = """
        CREATE TABLE IF NOT EXISTS FactRevisions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Key TEXT NOT NULL DEFAULT '',
            Value TEXT NOT NULL DEFAULT '',
            Source TEXT NOT NULL DEFAULT '',
            SourceRef TEXT,
            ValidFrom TEXT NOT NULL DEFAULT '0001-01-01',
            ValidTo TEXT NOT NULL DEFAULT '0001-01-01',
            Change TEXT NOT NULL DEFAULT 'replaced'
        );
        CREATE INDEX IF NOT EXISTS IX_FactRevisions_Key ON FactRevisions(Key);

        CREATE TABLE IF NOT EXISTS Entities (
            Id TEXT PRIMARY KEY,
            Type TEXT NOT NULL DEFAULT 'other',
            Name TEXT NOT NULL DEFAULT '',
            NameKey TEXT NOT NULL DEFAULT '',
            Aliases TEXT NOT NULL DEFAULT '',
            Note TEXT,
            CreatedAt TEXT NOT NULL DEFAULT '0001-01-01',
            UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01'
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Entities_NameKey ON Entities(NameKey);

        CREATE TABLE IF NOT EXISTS Links (
            Id TEXT PRIMARY KEY,
            FromType TEXT NOT NULL DEFAULT '',
            FromId TEXT NOT NULL DEFAULT '',
            Relation TEXT NOT NULL DEFAULT 'mentions',
            ToType TEXT NOT NULL DEFAULT '',
            ToId TEXT NOT NULL DEFAULT '',
            Basis TEXT NOT NULL DEFAULT 'observed',
            Source TEXT NOT NULL DEFAULT 'agent',
            SourceRef TEXT,
            CreatedAt TEXT NOT NULL DEFAULT '0001-01-01'
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Links_FromType_FromId_Relation_ToType_ToId
            ON Links(FromType, FromId, Relation, ToType, ToId);
        CREATE INDEX IF NOT EXISTS IX_Links_ToType_ToId ON Links(ToType, ToId);
        """;

    // Every memory that existed before the lifecycle did was in use as fact, so it starts
    // active: the default is what keeps San's recall unchanged across the upgrade.
    private static readonly (string Table, string Column, string Definition)[] Columns =
    [
        ("Memories", "Status", "TEXT NOT NULL DEFAULT 'active'"),
        ("Memories", "Supersedes", "TEXT"),
        ("Memories", "SupersededBy", "TEXT"),
        ("Memories", "StatusChangedAt", "TEXT"),
        ("Memories", "StatusNote", "TEXT"),
        ("Memories", "SourceRef", "TEXT"),
        ("Memories", "Evidence", "TEXT"),
        ("Memories", "ContentKey", "TEXT NOT NULL DEFAULT ''"),
        ("Facts", "SourceRef", "TEXT"),
    ];

    public static async Task UpgradeAsync(DbConnection conn, CancellationToken ct = default)
    {
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await ExecAsync(conn, TablesSql, ct);

        foreach (var (table, column, definition) in Columns)
            if (!await HasColumnAsync(conn, table, column, ct))
                await ExecAsync(conn, $"ALTER TABLE {table} ADD COLUMN {column} {definition}", ct);

        await ExecAsync(conn, """
            CREATE INDEX IF NOT EXISTS IX_Memories_Status ON Memories(Status);
            CREATE INDEX IF NOT EXISTS IX_Memories_ContentKey ON Memories(ContentKey);
            """, ct);

        await BackfillContentKeysAsync(conn, ct);
    }

    // Memories written before ContentKey existed get theirs, so the duplicate check sees
    // them; without it the first restatement of an old memory would be stored twice.
    private static async Task BackfillContentKeysAsync(DbConnection conn, CancellationToken ct)
    {
        var pending = new List<(string Id, string Content)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Content FROM Memories WHERE ContentKey = ''";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) pending.Add((r.GetString(0), r.GetString(1)));
        }

        foreach (var (id, content) in pending)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Memories SET ContentKey = $key WHERE Id = $id";
            Add(cmd, "$key", MemorySearch.Normalise(content));
            Add(cmd, "$id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<bool> HasColumnAsync(DbConnection conn, string table, string column, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $col";
        Add(cmd, "$col", column);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task ExecAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
