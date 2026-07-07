using Maaya.Auth;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

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
    ";
    await cmd.ExecuteNonQueryAsync();
}

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
app.Run("http://localhost:5500");
