using Maaya.Auth;
using Microsoft.EntityFrameworkCore;
using Vault.Worker.Data;
using Vault.Worker.Services;

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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMaayaAuth(isAuthServer: true);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:5173"))
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var connectionString = builder.Configuration.GetConnectionString("VaultDb") ?? "Data Source=vault.db";
builder.Services.AddDbContext<VaultDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<IPlaidService, PlaidService>();
builder.Services.AddScoped<ISyncService, SyncService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VaultDbContext>();
    await db.Database.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseMaayaAuth();
app.MapControllers();

// Default: launchSettings port (dev). BIND_URL=http://0.0.0.0:5000 in containers.
var bindUrl = Environment.GetEnvironmentVariable("BIND_URL");
if (bindUrl is not null) app.Run(bindUrl); else app.Run();
