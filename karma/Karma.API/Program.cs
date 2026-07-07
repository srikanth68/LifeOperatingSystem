using Karma.API.Services;
using Karma.Application.Interfaces;
using Karma.Infrastructure.Data;
using Karma.Infrastructure.Notifications;
using Maaya.Auth;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddMaayaAuth();

builder.Services.AddDbContext<KarmaDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "karma.db")}"));

builder.Services.AddScoped<IKarmaRepository, KarmaRepository>();
builder.Services.AddHttpClient<INotificationSender, NotificationSender>();
builder.Services.AddHostedService<HabitNotificationService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:3000", "http://localhost:3001", "http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KarmaDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseCors();
app.UseMaayaAuth();
app.MapControllers();
app.Run("http://localhost:5600");
