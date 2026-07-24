using Maaya.Auth;
using Microsoft.EntityFrameworkCore;
using San.Application.Interfaces;
using San.Infrastructure.Agent;
using San.Infrastructure.Chat;
using San.Infrastructure.Context;
using San.Infrastructure.Data;
using San.Infrastructure.Google;
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

builder.Configuration["Llm:Provider"] = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "gemini";
builder.Configuration["Llm:Model"]    = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gemini-2.0-flash";

var vaultUrl     = Environment.GetEnvironmentVariable("VAULT_API_URL")     ?? "http://localhost:5000";
var vitaraUrl    = Environment.GetEnvironmentVariable("VITARA_API_URL")    ?? "http://localhost:5100";
var aasthiUrl    = Environment.GetEnvironmentVariable("AASTHI_API_URL")    ?? "http://localhost:5200";
var northstarUrl = Environment.GetEnvironmentVariable("NORTHSTAR_API_URL") ?? "http://localhost:5500";
var sutraUrl     = Environment.GetEnvironmentVariable("SUTRA_API_URL")     ?? "http://localhost:5400";
var karmaUrl     = Environment.GetEnvironmentVariable("KARMA_API_URL")     ?? "http://localhost:5600";
var nexusUrl     = Environment.GetEnvironmentVariable("NEXUS_API_URL")     ?? "http://localhost:5700";

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();
builder.Services.AddDbContext<SanDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "san.db")}"));
builder.Services.AddScoped<ISanRepository, SanRepository>();
builder.Services.AddScoped<IModuleContextService, ModuleContextService>();
builder.Services.AddScoped<IChatActionService, ChatActionService>();
builder.Services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IContextReceiver, ContextReceiver>();
builder.Services.AddHttpClient<ITelegramNotifier, TelegramNotifier>();

// LLM provider selection — purely config-driven so the model/provider can change without
// touching code. Add another `case` + implementation to support a non-Anthropic provider.
switch (builder.Configuration["Llm:Provider"])
{
    case "gemini":
        builder.Services.AddHttpClient<IChatProvider, GeminiChatProvider>();
        break;
    case "ollama":
        builder.Services.AddHttpClient<IChatProvider, OllamaChatProvider>();
        break;
    case "llamacpp":
    case "openai-compatible":
        builder.Services.AddHttpClient<IChatProvider, LlamaCppChatProvider>();
        break;
    case "hermes":
        // Agent gateway — a full tool-calling loop runs per request, so give it room.
        builder.Services.AddHttpClient<IChatProvider, HermesChatProvider>(c => c.Timeout = TimeSpan.FromMinutes(5));
        break;
    case "anthropic":
    default:
        builder.Services.AddHttpClient<IChatProvider, AnthropicChatProvider>();
        break;
}

builder.Services.AddHttpClient("vault",     c => c.BaseAddress = new Uri(vaultUrl));
builder.Services.AddHttpClient("vitara",    c => c.BaseAddress = new Uri(vitaraUrl));
builder.Services.AddHttpClient("aasthi",    c => c.BaseAddress = new Uri(aasthiUrl));
builder.Services.AddHttpClient("northstar", c => c.BaseAddress = new Uri(northstarUrl));
builder.Services.AddHttpClient("sutra",     c => c.BaseAddress = new Uri(sutraUrl));
builder.Services.AddHttpClient("karma",     c => c.BaseAddress = new Uri(karmaUrl));
builder.Services.AddHttpClient("nexus",     c => c.BaseAddress = new Uri(nexusUrl));
// Voice engines (self-hosted, OpenAI-compatible). Absolute URLs are built per-request
// from WHISPER_SERVICE_URL / PIPER_SERVICE_URL, so no base address here — the clients
// just carry sensible timeouts (speech synthesis of a long reply can take a few seconds).
// Whisper gets a long timeout: the first transcription after a cold start also loads
// the model, which on CPU can take well over a minute.
builder.Services.AddHttpClient("whisper", c => c.Timeout = TimeSpan.FromSeconds(300));
builder.Services.AddHttpClient("piper",   c => c.Timeout = TimeSpan.FromSeconds(120));

builder.Services.AddScoped<AgentToolExecutor>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:5173")).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SanDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS People (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL DEFAULT '',
            Phone TEXT,
            Email TEXT,
            Birthday TEXT,
            Relationship TEXT NOT NULL DEFAULT 'other',
            Notes TEXT,
            Tags TEXT,
            CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00',
            UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
        );
        CREATE INDEX IF NOT EXISTS IX_People_Name ON People(Name);
        CREATE INDEX IF NOT EXISTS IX_People_Birthday ON People(Birthday);
        CREATE TABLE IF NOT EXISTS Settings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL DEFAULT ''
        );
        """);
}

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
app.Run(Environment.GetEnvironmentVariable("BIND_URL") ?? "http://localhost:5300");
