using System.Globalization;
using Maaya.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitara.Application.Interfaces;
using Vitara.Infrastructure.Data;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Oura;
using Vitara.Infrastructure.Profiles;

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
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddMaayaAuth();
// One database file per person. The original vitara.db is the profile called "default" and
// is what every request without a profile still reaches, so nothing existing changes.
builder.Services.AddVitaraProfiles();
builder.Services.AddScoped<IVitaraRepository, VitaraRepository>();
builder.Services.AddScoped<IOuraClient, OuraClient>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:5173"))
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

// Everybody's database is brought up to the current schema, not only the original: a column
// added in this release has to exist in every file. Reference ranges are inserted only where
// missing, so a range edited to match somebody's own lab report survives every restart.
await ProfileDatabases.EnsureAllAsync(app.Services, app.Logger);

// The one-off day-format repair is for data written before ISO days existed, which only the
// original database can contain.
using (var scope = app.Services.CreateScope())
    await NormalizeDayColumnsAsync(scope.ServiceProvider.GetRequiredService<VitaraDbContext>());

app.UseCors();
app.UseMaayaAuth();

// Which person is this request about. Before anything that opens a database, because the
// connection is chosen when the context is created. No profile means the original one.
app.Use(async (ctx, next) =>
{
    var resolved = ProfileResolver.Resolve(
        ctx.Request.Headers["X-Profile-Id"].FirstOrDefault(),
        ctx.Request.Query["profile"].FirstOrDefault(),
        ctx.Request.Query["state"].FirstOrDefault(),
        ctx.Request.Path.Value ?? "");

    if (resolved.Error is not null)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = resolved.Error });
        return;
    }

    if (!ctx.RequestServices.GetRequiredService<ProfileCatalog>().Exists(resolved.Id))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsJsonAsync(new { error = "There is no such profile." });
        return;
    }

    ctx.RequestServices.GetRequiredService<ProfileContext>().Use(resolved.Id);
    await next();
});

app.MapControllers();
app.Run(Environment.GetEnvironmentVariable("BIND_URL") ?? "http://localhost:5100");

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

