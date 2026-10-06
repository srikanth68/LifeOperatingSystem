using Microsoft.EntityFrameworkCore;
using Vitara.Application.Interfaces;
using Vitara.Infrastructure.Nutrition;
using Vitara.Infrastructure.Data;
using Vitara.Infrastructure.Oura;
using Vitara.Worker;
using Vitara.Infrastructure.Profiles;

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
// One database per person; see ProfileServices. The original vitara.db is "default".
builder.Services.AddVitaraProfiles();
builder.Services.AddScoped<IVitaraRepository, VitaraRepository>();
builder.Services.AddScoped<IOuraClient, OuraClient>();
builder.Services.AddScoped<INutritionSource, MfpNutritionClient>();
builder.Services.AddHttpClient("mfp");
builder.Services.AddHostedService<OuraSyncWorker>();
builder.Services.AddHostedService<NutritionSyncWorker>();

var host = builder.Build();

// Everybody's file, not only the original: a column added in this release has to exist in all.
await ProfileDatabases.EnsureAllAsync(host.Services);

await host.RunAsync();
