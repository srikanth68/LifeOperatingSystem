using Aasthi.Application.Interfaces;
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

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();
builder.Services.AddDbContext<AasthiDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "aasthi.db")}"));
builder.Services.AddScoped<IAasthiRepository, AasthiRepository>();
builder.Services.AddSingleton<IDocumentStorage>(new FileDocumentStorage(storageRoot));
builder.Services.AddHttpClient("sutra", c => c.BaseAddress = new Uri(sutraUrl));
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

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA table_info(Documents)";
    var columns = new HashSet<string>();
    using (var reader = await cmd.ExecuteReaderAsync())
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));

    if (!columns.Contains("SutraDocumentId"))
    {
        using var alter = conn.CreateCommand();
        alter.CommandText = "ALTER TABLE Documents ADD COLUMN SutraDocumentId TEXT";
        await alter.ExecuteNonQueryAsync();
    }

    if (opened) await conn.CloseAsync();
}
