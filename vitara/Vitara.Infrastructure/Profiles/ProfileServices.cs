using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Data;

namespace Vitara.Infrastructure.Profiles;

public static class ProfileServices
{
    // Replaces the single hard-coded database with one chosen per request.
    //
    // The options are built when a context is CREATED, per scope, so each request opens the
    // file for the profile its scope was told about. This is the only place a connection
    // string is decided, which is what makes it possible to say that nothing else in the
    // system can open the wrong person's data.
    public static IServiceCollection AddVitaraProfiles(this IServiceCollection services, ProfilePaths? paths = null)
    {
        // `paths` exists so a test can point the whole system at a temporary directory.
        services.AddSingleton(paths ?? ProfilePaths.FromWorkingDirectory());
        services.AddSingleton<ProfileCatalog>();
        services.AddScoped<ProfileContext>();

        services.AddDbContext<VitaraDbContext>((sp, options) =>
        {
            var file = sp.GetRequiredService<ProfilePaths>()
                .DatabaseFile(sp.GetRequiredService<ProfileContext>().ProfileId);

            options.UseSqlite($"Data Source={file}");
        });

        return services;
    }
}

public static class ProfileScopes
{
    // Runs the same work once per person, each in a scope that has been told who it is for.
    //
    // One person failing must not stop the others: the sync worker can hit an expired Oura
    // token for one profile and still need to refresh everybody else's, and a loop that
    // threw on the first problem would turn one person's broken link into everyone's.
    public static async Task ForEachAsync(
        IServiceProvider root, Func<IServiceScope, string, Task> work, ILogger? log = null)
    {
        var catalog = root.GetRequiredService<ProfileCatalog>();

        foreach (var person in catalog.List())
        {
            using var scope = root.CreateScope();

            // Before anything that touches the database is resolved from this scope.
            scope.ServiceProvider.GetRequiredService<ProfileContext>().Use(person.Id);

            try { await work(scope, person.Id); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { log?.LogError(ex, "Work for profile {Profile} failed; continuing with the others.", person.Id); }
        }
    }
}

public static class ProfileDatabases
{
    // Brings every person's database up to the current schema. Idempotent, so safe on every
    // start, and run for everyone because a column added in this release has to exist in
    // every file, not only in the original one.
    public static Task EnsureAllAsync(IServiceProvider root, ILogger? log = null) =>
        ProfileScopes.ForEachAsync(root, async (scope, id) =>
        {
            var db = scope.ServiceProvider.GetRequiredService<VitaraDbContext>();
            await db.Database.EnsureCreatedAsync();
            await VitaraDbContext.CreateMissingTablesAsync(db);

            // Inserted only where missing, so a range somebody corrected survives every start.
            await scope.ServiceProvider.GetRequiredService<IVitaraRepository>()
                .SeedReferenceRangesAsync(ReferenceRanges.Seed);
        }, log);
}
