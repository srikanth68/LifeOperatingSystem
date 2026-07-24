using Maaya.Auth;
using Microsoft.EntityFrameworkCore;
using Sutra.Application.Interfaces;
using Sutra.Infrastructure.Data;
using Sutra.Infrastructure.Storage;

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

builder.Services.AddControllers();
builder.Services.AddMaayaAuth();
builder.Services.AddDbContext<SutraDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "sutra.db")}"));
builder.Services.AddScoped<ISutraRepository, SutraRepository>();
builder.Services.AddSingleton<IDocumentStorage>(new FileDocumentStorage(storageRoot));
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(MaayaCors.Origins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173"))
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SutraDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
app.Run(Environment.GetEnvironmentVariable("BIND_URL") ?? "http://localhost:5400");
