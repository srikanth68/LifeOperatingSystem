using Aasthi.Application.Interfaces;
using Aasthi.Infrastructure.ModuleClients;
using Aasthi.Infrastructure.Data;
using Aasthi.Infrastructure.Storage;
using Maaya.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var envFile = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
if (File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
        var idx = line.IndexOf('=');
        if (idx > 0) Environment.SetEnvironmentVariable(line[..idx].Trim(), line[(idx + 1)..].Trim());
    }
}

var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "storage");
Directory.CreateDirectory(storageRoot);

var sutraUrl = Environment.GetEnvironmentVariable("SUTRA_API_URL") ?? "http://localhost:5400";
var vaultUrl = Environment.GetEnvironmentVariable("VAULT_API_URL") ?? "http://localhost:5000";

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();
builder.Services.AddDbContext<AasthiDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "aasthi.db")}"));
builder.Services.AddScoped<IAasthiRepository, AasthiRepository>();
builder.Services.AddSingleton<IDocumentStorage>(new FileDocumentStorage(storageRoot));
builder.Services.AddHttpClient("sutra", c => c.BaseAddress = new Uri(sutraUrl));
// Bank transactions live in Vault. Aasthi reads them to reconcile what a property was
// due against what actually moved; it never writes there.
builder.Services.AddHttpClient("vault", c => c.BaseAddress = new Uri(vaultUrl));
builder.Services.AddScoped<IVaultTransactions, VaultTransactionClient>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173"))
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AasthiDbContext>();
    await db.Database.EnsureCreatedAsync();
    await AddMissingColumnsAsync(db);
}

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
app.Run(Environment.GetEnvironmentVariable("BIND_URL") ?? "http://localhost:5200");

static async Task AddMissingColumnsAsync(AasthiDbContext db)
{
    var conn = (SqliteConnection)db.Database.GetDbConnection();
    var opened = conn.State != System.Data.ConnectionState.Open;
    if (opened) await conn.OpenAsync();

    // EnsureCreated only builds the schema when the file does not exist, so on any
    // database that already holds properties it is a no-op -- new tables and columns
    // have to be added by hand here or they simply never appear in production.
    await AddColumnAsync(conn, "Documents", "SutraDocumentId", "TEXT");

    // Bank reconciliation and tax fields on the existing money table. Every one of
    // these carries a DEFAULT: SQLite backfills existing rows with it, and without
    // that the entries created before today would come back NULL into non-nullable
    // string properties and throw on the first read.
    //
    // The defaults are also the semantically right answer for old rows -- they really
    // were entered by hand, they really are confirmed, and their tax treatment really
    // is unknown until someone says otherwise.
    await AddColumnAsync(conn, "FinancialEntries", "VaultTransactionId", "TEXT");
    await AddColumnAsync(conn, "FinancialEntries", "Origin", "TEXT NOT NULL DEFAULT 'manual'");
    await AddColumnAsync(conn, "FinancialEntries", "Status", "TEXT NOT NULL DEFAULT 'confirmed'");
    await AddColumnAsync(conn, "FinancialEntries", "RecurringChargeId", "TEXT");
    await AddColumnAsync(conn, "FinancialEntries", "MatchConfidence", "INTEGER");
    await AddColumnAsync(conn, "FinancialEntries", "TaxTreatment", "TEXT NOT NULL DEFAULT 'unclassified'");
    await AddColumnAsync(conn, "FinancialEntries", "ReceiptDocumentId", "TEXT");
    await AddColumnAsync(conn, "FinancialEntries", "ConfirmedAt", "TEXT");

    using (var create = conn.CreateCommand())
    {
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS RecurringCharges (
                Id          TEXT PRIMARY KEY,
                PropertyId  TEXT NOT NULL,
                Direction   TEXT NOT NULL DEFAULT 'expense',
                Category    TEXT NOT NULL DEFAULT 'other',
                Amount      TEXT NOT NULL DEFAULT '0',
                Frequency   TEXT NOT NULL DEFAULT 'monthly',
                DueDay      INTEGER NOT NULL DEFAULT 1,
                StartDate   TEXT NOT NULL,
                EndDate     TEXT NULL,
                MatchHint   TEXT NULL,
                Active      INTEGER NOT NULL DEFAULT 1,
                Notes       TEXT NULL,
                CreatedAt   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RecurringCharges_Property ON RecurringCharges (PropertyId, Active);
            CREATE INDEX IF NOT EXISTS IX_FinancialEntries_VaultTx ON FinancialEntries (VaultTransactionId);
            CREATE INDEX IF NOT EXISTS IX_FinancialEntries_Status  ON FinancialEntries (Status);
            """;
        await create.ExecuteNonQueryAsync();
    }

    if (opened) await conn.CloseAsync();
}

// Adds a column only when it is missing. SQLite has no ADD COLUMN IF NOT EXISTS, and
// running the ALTER unconditionally throws on every start after the first.
static async Task AddColumnAsync(SqliteConnection conn, string table, string column, string spec)
{
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var probe = conn.CreateCommand())
    {
        probe.CommandText = $"PRAGMA table_info({table})";
        using var reader = await probe.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
    }

    // No rows means the table itself does not exist yet -- EnsureCreated will have
    // built it from the model on a fresh database, so there is nothing to patch.
    if (columns.Count == 0 || columns.Contains(column)) return;

    using var alter = conn.CreateCommand();
    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {spec}";
    await alter.ExecuteNonQueryAsync();
}
