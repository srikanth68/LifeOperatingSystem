using Maaya.Auth;
using Nexus.Application.Interfaces;
using Nexus.Infrastructure;

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

// Two ways to reach Sentinel:
//   1. SENTINEL_API_URL set  -> remote engine exposes its own HTTP/JSON API
//      (e.g. another machine over the VPN tunnel at http://127.0.0.1:8787).
//   2. otherwise             -> read a co-located sentinel.db file directly.
var sentinelApiUrl = Environment.GetEnvironmentVariable("SENTINEL_API_URL");

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();

if (!string.IsNullOrWhiteSpace(sentinelApiUrl))
{
    var prefix = Environment.GetEnvironmentVariable("SENTINEL_API_PREFIX") ?? "";
    builder.Services.AddHttpClient("sentinel", c => c.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddSingleton<ISentinelReader>(sp =>
        new SentinelApiReader(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("sentinel"),
            sentinelApiUrl, prefix));
}
else
{
    var sentinelDbPath = Environment.GetEnvironmentVariable("SENTINEL_DB_PATH")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "sentinel", "sentinel.db");
    if (!Path.IsPathRooted(sentinelDbPath))
        sentinelDbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), sentinelDbPath));
    builder.Services.AddSingleton<ISentinelReader>(new SentinelDbReader(sentinelDbPath));
}
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173"))
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
app.Run(Environment.GetEnvironmentVariable("BIND_URL") ?? "http://localhost:5700");
