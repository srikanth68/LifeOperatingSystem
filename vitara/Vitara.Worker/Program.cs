using Microsoft.EntityFrameworkCore;
using Vitara.Application.Interfaces;
using Vitara.Infrastructure.Data;
using Vitara.Infrastructure.Oura;
using Vitara.Worker;

// Load .env
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

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration["Oura:ClientId"]     = Environment.GetEnvironmentVariable("OURA_CLIENT_ID") ?? "";
builder.Configuration["Oura:ClientSecret"] = Environment.GetEnvironmentVariable("OURA_CLIENT_SECRET") ?? "";
builder.Configuration["Oura:RedirectUri"]  = Environment.GetEnvironmentVariable("OURA_REDIRECT_URI")
    ?? "http://localhost:5100/api/oura/callback";

builder.Services.AddHttpClient();
builder.Services.AddDbContext<VitaraDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "vitara.db")}"));
builder.Services.AddScoped<IVitaraRepository, VitaraRepository>();
builder.Services.AddScoped<IOuraClient, OuraClient>();
builder.Services.AddHostedService<OuraSyncWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VitaraDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await host.RunAsync();
