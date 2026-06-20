using Microsoft.EntityFrameworkCore;
using San.Application.Interfaces;
using San.Infrastructure.Data;
using San.Infrastructure.ModuleClients;
using San.Infrastructure.Notifications;
using San.Worker;

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

var vaultUrl  = Environment.GetEnvironmentVariable("VAULT_API_URL")  ?? "http://localhost:5000";
var vitaraUrl = Environment.GetEnvironmentVariable("VITARA_API_URL") ?? "http://localhost:5100";
var aasthiUrl = Environment.GetEnvironmentVariable("AASTHI_API_URL") ?? "http://localhost:5200";

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<SanDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "san.db")}"));
builder.Services.AddScoped<ISanRepository, SanRepository>();
builder.Services.AddScoped<IModuleContextService, ModuleContextService>();
builder.Services.AddHttpClient<ITelegramNotifier, TelegramNotifier>();
builder.Services.AddHttpClient("vault",  c => c.BaseAddress = new Uri(vaultUrl));
builder.Services.AddHttpClient("vitara", c => c.BaseAddress = new Uri(vitaraUrl));
builder.Services.AddHttpClient("aasthi", c => c.BaseAddress = new Uri(aasthiUrl));
builder.Services.AddHostedService<NotificationWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SanDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await host.RunAsync();
