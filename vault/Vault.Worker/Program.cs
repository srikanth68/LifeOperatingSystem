using Microsoft.EntityFrameworkCore;
using Serilog;
using Vault.Worker.Data;
using Vault.Worker.Jobs;
using Vault.Worker.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/vault-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((context, services) =>
        {
            var connectionString = context.Configuration.GetConnectionString("VaultDb") ?? "Data Source=vault.db";
            services.AddDbContext<VaultDbContext>(options =>
                options.UseSqlite(connectionString));

            services.AddScoped<IPlaidService, PlaidService>();
            services.AddScoped<ISyncService, SyncService>();
            services.AddHostedService<ScheduledSyncWorker>();
        });

    var host = builder.Build();

    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<VaultDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
