using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitara.Application.Interfaces;
using Vitara.Infrastructure.Data;
using Vitara.Infrastructure.Oura;

var builder = WebApplication.CreateBuilder(args);

// Load .env from project root
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

builder.Configuration["Oura:ClientId"]     = Environment.GetEnvironmentVariable("OURA_CLIENT_ID") ?? "";
builder.Configuration["Oura:ClientSecret"] = Environment.GetEnvironmentVariable("OURA_CLIENT_SECRET") ?? "";
builder.Configuration["Oura:RedirectUri"]  = Environment.GetEnvironmentVariable("OURA_REDIRECT_URI")
    ?? "http://localhost:5100/api/oura/callback";
builder.Configuration["Vitara:ChronologicalAge"] = Environment.GetEnvironmentVariable("VITARA_AGE") ?? "30";

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddDbContext<VitaraDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "vitara.db")}"));
builder.Services.AddScoped<IVitaraRepository, VitaraRepository>();
builder.Services.AddScoped<IOuraClient, OuraClient>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:3000", "http://localhost:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VitaraDbContext>();
    await db.Database.EnsureCreatedAsync();
    await NormalizeDayColumnsAsync(db);
}

app.UseCors();
app.MapControllers();
app.Run("http://localhost:5100");

// One-time (idempotent) data fix: older rows stored `Day` using DateOnly.ToString()'s
// culture short-date format (e.g. "6/9/2026", no leading zeros). SQLite compares that
// TEXT column lexicographically, not chronologically, so range filters like
// `Day >= from && Day <= to` silently dropped rows — see VitaraDbContext.DayFormat for
// details. This rewrites any non-ISO Day values to "yyyy-MM-dd" in place. Safe to run on
// every startup: rows already in ISO format are skipped.
static async Task NormalizeDayColumnsAsync(VitaraDbContext db)
{
    var conn = (SqliteConnection)db.Database.GetDbConnection();
    var opened = conn.State != System.Data.ConnectionState.Open;
    if (opened) await conn.OpenAsync();

    foreach (var table in new[] { "Sleep", "Readiness", "Activity" })
    {
        var updates = new List<(string Id, string NewDay)>();

        using (var select = conn.CreateCommand())
        {
            select.CommandText = $"SELECT Id, Day FROM {table}";
            using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                var day = reader.GetString(1);
                if (DateOnly.TryParseExact(day, VitaraDbContext.DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    continue; // already normalized

                if (!DateOnly.TryParse(day, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    continue; // unparseable; leave as-is rather than corrupt it

                updates.Add((id, parsed.ToString(VitaraDbContext.DayFormat, CultureInfo.InvariantCulture)));
            }
        }

        foreach (var (id, newDay) in updates)
        {
            using var update = conn.CreateCommand();
            update.CommandText = $"UPDATE {table} SET Day = @day WHERE Id = @id";
            update.Parameters.AddWithValue("@day", newDay);
            update.Parameters.AddWithValue("@id", id);
            await update.ExecuteNonQueryAsync();
        }
    }

    if (opened) await conn.CloseAsync();
}
