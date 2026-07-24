using Karma.API.Services;
using Karma.Application.Interfaces;
using Karma.Infrastructure.Data;
using Karma.Infrastructure.Notifications;
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

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();

builder.Services.AddDbContext<KarmaDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "karma.db")}"));

builder.Services.AddScoped<IKarmaRepository, KarmaRepository>();
builder.Services.AddHttpClient<INotificationSender, NotificationSender>();
builder.Services.AddHostedService<HabitNotificationService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173"))
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KarmaDbContext>();
    await db.Database.EnsureCreatedAsync();
    await AddMissingColumnsAsync(db);
}

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
app.Run(Environment.GetEnvironmentVariable("BIND_URL") ?? "http://localhost:5600");

// EnsureCreated doesn't migrate schema changes on existing DBs; add new nullable
// columns by hand so pre-existing karma.db files pick them up.
static async Task AddMissingColumnsAsync(KarmaDbContext db)
{
    var conn = (SqliteConnection)db.Database.GetDbConnection();
    var opened = conn.State != System.Data.ConnectionState.Open;
    if (opened) await conn.OpenAsync();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA table_info(Habits)";
    var columns = new HashSet<string>();
    using (var reader = await cmd.ExecuteReaderAsync())
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));

    if (!columns.Contains("GoalId"))
    {
        using var alter = conn.CreateCommand();
        alter.CommandText = "ALTER TABLE Habits ADD COLUMN GoalId TEXT";
        await alter.ExecuteNonQueryAsync();
    }

    if (opened) await conn.CloseAsync();
}
