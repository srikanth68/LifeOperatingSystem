using System.Security.Cryptography;
using System.Text;
using Maaya.Auth;
using Maaya.Mcp;
using Maaya.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Load .env from module root (same convention as every Maaya module).
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

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException("JWT_SECRET is required (shared Maaya secret — see vault/.env).");
var apiKey = Environment.GetEnvironmentVariable("MCP_API_KEY");
var bind = Environment.GetEnvironmentVariable("MCP_BIND") ?? "http://localhost:5900";

// Security invariant: never expose the gateway beyond localhost without a key.
var localOnly = bind.Contains("localhost") || bind.Contains("127.0.0.1");
if (!localOnly && string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException(
        "MCP_API_KEY is required when MCP_BIND is not localhost (the gateway would be open to the whole network/Meshnet).");

// Service JWT minting for downstream module calls (same shared-secret trust as all modules).
builder.Services.AddSingleton(new JwtConfig { Secret = jwtSecret });
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ModuleGateway>();

// One named HttpClient per module. URLs env-overridable so the gateway can run
// anywhere (Dev laptop today, Everest later) and point at modules wherever they live.
var modules = new Dictionary<string, string>
{
    ["vault"] = Environment.GetEnvironmentVariable("VAULT_API_URL") ?? "http://localhost:5000",
    ["vitara"] = Environment.GetEnvironmentVariable("VITARA_API_URL") ?? "http://localhost:5100",
    ["aasthi"] = Environment.GetEnvironmentVariable("AASTHI_API_URL") ?? "http://localhost:5200",
    ["san"] = Environment.GetEnvironmentVariable("SAN_API_URL") ?? "http://localhost:5300",
    ["sutra"] = Environment.GetEnvironmentVariable("SUTRA_API_URL") ?? "http://localhost:5400",
    ["northstar"] = Environment.GetEnvironmentVariable("NORTHSTAR_API_URL") ?? "http://localhost:5500",
    ["karma"] = Environment.GetEnvironmentVariable("KARMA_API_URL") ?? "http://localhost:5600",
    ["nexus"] = Environment.GetEnvironmentVariable("NEXUS_API_URL") ?? "http://localhost:5700",
};
foreach (var (name, url) in modules)
    builder.Services.AddHttpClient(name, c =>
    {
        c.BaseAddress = new Uri(url);
        c.Timeout = TimeSpan.FromSeconds(8);
    });

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<MemoryTools>()
    .WithTools<ModuleTools>()
    .WithTools<ActionTools>();

var app = builder.Build();

// API-key gate (constant-time compare). /health stays open for probes.
if (!string.IsNullOrWhiteSpace(apiKey))
{
    var keyBytes = Encoding.UTF8.GetBytes(apiKey);
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/health")) { await next(); return; }

        var presented = ctx.Request.Headers["X-API-Key"].ToString();
        if (string.IsNullOrEmpty(presented))
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.Ordinal)) presented = auth["Bearer ".Length..];
        }

        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var ok = presentedBytes.Length == keyBytes.Length
                 && CryptographicOperations.FixedTimeEquals(presentedBytes, keyBytes);
        if (!ok)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "unauthorized — pass MCP_API_KEY as Bearer token or X-API-Key header" });
            return;
        }
        await next();
    });
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    server = "maaya-mcp",
    secured = !string.IsNullOrWhiteSpace(apiKey),
    modules = modules.Keys,
}));

app.MapMcp();

app.Run(bind);
