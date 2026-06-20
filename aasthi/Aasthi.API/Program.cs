using Aasthi.Application.Interfaces;
using Aasthi.Infrastructure.Data;
using Aasthi.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "storage");
Directory.CreateDirectory(storageRoot);

builder.Services.AddControllers();
builder.Services.AddDbContext<AasthiDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(Directory.GetCurrentDirectory(), "..", "aasthi.db")}"));
builder.Services.AddScoped<IAasthiRepository, AasthiRepository>();
builder.Services.AddSingleton<IDocumentStorage>(new FileDocumentStorage(storageRoot));
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:3000", "http://localhost:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AasthiDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseCors();
app.MapControllers();
app.Run("http://localhost:5200");
