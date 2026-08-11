using Maaya.Auth;
using Microsoft.EntityFrameworkCore;
using San.Application.Interfaces;
using San.Infrastructure.Agent;
using San.Infrastructure.Chat;
using San.Infrastructure.Data;
using San.Infrastructure.Health;
using San.Infrastructure.Google;
using San.Infrastructure.Llm;
using San.Infrastructure.ModuleClients;
using San.Infrastructure.Notifications;
using San.Infrastructure.Outlook;
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

var vaultUrl     = Environment.GetEnvironmentVariable("VAULT_API_URL")     ?? "http://localhost:5000";
var vitaraUrl    = Environment.GetEnvironmentVariable("VITARA_API_URL")    ?? "http://localhost:5100";
var aasthiUrl    = Environment.GetEnvironmentVariable("AASTHI_API_URL")    ?? "http://localhost:5200";
var northstarUrl = Environment.GetEnvironmentVariable("NORTHSTAR_API_URL") ?? "http://localhost:5500";

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration["Llm:Provider"] = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "gemini";
builder.Configuration["Llm:Model"]    = Environment.GetEnvironmentVariable("LLM_MODEL") ?? "gemini-2.0-flash";

builder.Services.AddDbContext<SanDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "san.db")}"));
builder.Services.AddScoped<ISanRepository, SanRepository>();
builder.Services.AddScoped<IHealthTracker, HealthTracker>();
builder.Services.AddScoped<IModuleContextService, ModuleContextService>();
builder.Services.AddHttpClient<ITelegramNotifier, TelegramNotifier>();

// TokenService (for minting the service JWT sibling modules require) — same shared
// secret as every module. Registered via AddMaayaAuth; the auth middleware bits it
// also wires up are harmless in a worker (no HTTP pipeline uses them).
builder.Services.AddMaayaAuth();

// LLM provider — mirrors San.API so the memory-distillation worker can call the
// same model the chat uses. Config-driven; add a case to support another provider.
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
    case "llamacpp-agent":
        // Worker only ever calls plain CompleteAsync — the provider's tool loop
        // simply isn't engaged. Registered so this mode doesn't fall to the cloud
        // default below (an unknown provider name silently gets the Anthropic
        // CLOUD provider — that's how chat history nearly left the machine once).
        builder.Services.AddHttpClient<IChatProvider, LlamaCppAgentChatProvider>();
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
builder.Services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IEmailProviderClient, GoogleGmailClient>();
builder.Services.AddHttpClient<IEmailProviderClient, MicrosoftGraphClient>();

// Same agent-tool stack as San.API's ChatController, so EmailTriageWorker can let
// Gemma tool-call reminders/alerts/calendar/property-tasks off triaged email.
builder.Services.AddScoped<IChatActionService, ChatActionService>();
builder.Services.AddScoped<AgentToolExecutor>();
builder.Services.AddHttpClient<McpToolClient>(c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddScoped<AgentToolRouter>();

builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHostedService<CalendarSyncWorker>();
builder.Services.AddHostedService<MemoryDistillationWorker>();
builder.Services.AddHostedService<EmailTriageWorker>();
builder.Services.AddHostedService<SystemAuditWorker>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SanDbContext>();
    // Same patch set as San.API — the worker must not depend on the API container
    // having started first to find its own columns.
    await SanSchema.EnsureAsync(db);
}

await host.RunAsync();
