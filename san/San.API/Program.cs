using Microsoft.EntityFrameworkCore;
using San.Application.Interfaces;
using San.Infrastructure.Data;
using San.Infrastructure.Llm;
using San.Infrastructure.ModuleClients;
using San.Infrastructure.Notifications;

var builder = WebApplication.CreateBuilder(args);

// Load .env from project root (same convention as Vitara/Aasthi).
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

builder.Configuration["Llm:Provider"] = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "anthropic";
builder.Configuration["Llm:Model"]    = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "claude-sonnet-4-6";

var vaultUrl  = Environment.GetEnvironmentVariable("VAULT_API_URL")  ?? "http://localhost:5000";
var vitaraUrl = Environment.GetEnvironmentVariable("VITARA_API_URL") ?? "http://localhost:5100";
var aasthiUrl = Environment.GetEnvironmentVariable("AASTHI_API_URL") ?? "http://localhost:5200";

builder.Services.AddControllers();
builder.Services.AddDbContext<SanDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "san.db")}"));
builder.Services.AddScoped<ISanRepository, SanRepository>();
builder.Services.AddScoped<IModuleContextService, ModuleContextService>();
builder.Services.AddHttpClient<ITelegramNotifier, TelegramNotifier>();

// LLM provider selection — purely config-driven so the model/provider can change without
// touching code. Add another `case` + implementation to support a non-Anthropic provider.
switch (builder.Configuration["Llm:Provider"])
{
    case "anthropic":
    default:
        builder.Services.AddHttpClient<IChatProvider, AnthropicChatProvider>();
        break;
}

builder.Services.AddHttpClient("vault",  c => c.BaseAddress = new Uri(vaultUrl));
builder.Services.AddHttpClient("vitara", c => c.BaseAddress = new Uri(vitaraUrl));
builder.Services.AddHttpClient("aasthi", c => c.BaseAddress = new Uri(aasthiUrl));

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:3000", "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SanDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseCors();
app.MapControllers();
app.Run("http://localhost:5300");
