using Maaya.Auth;
using Microsoft.EntityFrameworkCore;
using NorthStar.API.Services;
using NorthStar.Application.Interfaces;
using NorthStar.Infrastructure.Data;

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

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();
builder.Services.AddDbContext<NorthStarDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "northstar.db")}"));
builder.Services.AddScoped<INorthStarRepository, NorthStarRepository>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ModuleSyncService>();
builder.Services.AddHostedService<NorthStarSyncWorker>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173")).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NorthStarDbContext>();
    await db.Database.EnsureCreatedAsync();
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Actions (
            Id TEXT PRIMARY KEY, Source TEXT NOT NULL DEFAULT 'manual',
            Category TEXT NOT NULL DEFAULT 'task', Title TEXT NOT NULL DEFAULT '',
            Description TEXT, Priority INTEGER NOT NULL DEFAULT 3,
            DueDate TEXT, Status TEXT NOT NULL DEFAULT 'pending',
            ResolvedBy TEXT, CreatedAt TEXT NOT NULL DEFAULT '0001-01-01', CompletedAt TEXT
        );
        CREATE TABLE IF NOT EXISTS Snapshots (
            Module TEXT PRIMARY KEY, SummaryJson TEXT NOT NULL DEFAULT '',
            CapturedAt TEXT NOT NULL DEFAULT '0001-01-01'
        );
        CREATE TABLE IF NOT EXISTS Facts (
            Key TEXT PRIMARY KEY, Value TEXT NOT NULL DEFAULT '',
            Source TEXT NOT NULL DEFAULT 'manual', UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01'
        );
        CREATE TABLE IF NOT EXISTS Events (
            Id TEXT PRIMARY KEY,
            Source TEXT NOT NULL DEFAULT '',
            Kind TEXT NOT NULL DEFAULT '',
            Title TEXT NOT NULL DEFAULT '',
            Detail TEXT,
            OccurredAt TEXT NOT NULL DEFAULT '0001-01-01',
            RecordedAt TEXT NOT NULL DEFAULT '0001-01-01',
            EventKey TEXT NOT NULL DEFAULT '',
            RawJson TEXT
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Events_EventKey ON Events(EventKey);
        CREATE INDEX IF NOT EXISTS IX_Events_OccurredAt ON Events(OccurredAt);
        CREATE INDEX IF NOT EXISTS IX_Events_Source ON Events(Source);
        CREATE TABLE IF NOT EXISTS Memories (
            Id TEXT PRIMARY KEY,
            Content TEXT NOT NULL DEFAULT '',
            Kind TEXT NOT NULL DEFAULT 'observation',
            Source TEXT NOT NULL DEFAULT 'agent',
            Tags TEXT NOT NULL DEFAULT '',
            Importance INTEGER NOT NULL DEFAULT 3,
            CreatedAt TEXT NOT NULL DEFAULT '0001-01-01',
            LastAccessedAt TEXT,
            AccessCount INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_Memories_Kind ON Memories(Kind);
        CREATE INDEX IF NOT EXISTS IX_Memories_CreatedAt ON Memories(CreatedAt);
        CREATE INDEX IF NOT EXISTS IX_Memories_Importance ON Memories(Importance);
        CREATE VIRTUAL TABLE IF NOT EXISTS MemoryFts USING fts5(Content, Tags, content='Memories', content_rowid='rowid');
        CREATE TRIGGER IF NOT EXISTS Memories_ai AFTER INSERT ON Memories BEGIN
            INSERT INTO MemoryFts(rowid, Content, Tags) VALUES (new.rowid, new.Content, new.Tags);
        END;
        CREATE TRIGGER IF NOT EXISTS Memories_ad AFTER DELETE ON Memories BEGIN
            INSERT INTO MemoryFts(MemoryFts, rowid, Content, Tags) VALUES ('delete', old.rowid, old.Content, old.Tags);
        END;
        CREATE TRIGGER IF NOT EXISTS Memories_au AFTER UPDATE OF Content, Tags ON Memories BEGIN
            INSERT INTO MemoryFts(MemoryFts, rowid, Content, Tags) VALUES ('delete', old.rowid, old.Content, old.Tags);
            INSERT INTO MemoryFts(rowid, Content, Tags) VALUES (new.rowid, new.Content, new.Tags);
        END;
    ";
    await cmd.ExecuteNonQueryAsync();
}

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
// Default localhost; set NORTHSTAR_BIND=http://0.0.0.0:5500 when agents on other
// machines (e.g. an external agent over Meshnet) need to reach the brain directly.
app.Run(Environment.GetEnvironmentVariable("NORTHSTAR_BIND") ?? "http://localhost:5500");
