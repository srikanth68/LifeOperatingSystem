using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Vitara.API.Controllers;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Data;
using Vitara.Infrastructure.Profiles;

namespace Vitara.Tests;

// More than one person, in one system, who must never see each other.
//
// The design is one database file per person, so the guarantees worth testing are not
// subtle query logic -- there is none -- but the three things that could still go wrong:
// the id (which becomes a file path and arrives from a browser), the isolation itself, and
// the original database surviving the change untouched.
public class ProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vitara-profiles-" + Guid.NewGuid().ToString("N"));
    private readonly ProfilePaths _paths;
    private readonly ProfileCatalog _catalog;

    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ProfileTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new ProfilePaths(_root);
        _catalog = new ProfileCatalog(_paths);
    }

    public void Dispose()
    {
        // A pooled connection holds the file open and a Windows delete then fails.
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static ProfileForm Form(string? name = "Asha", string? sex = null, DateOnly? born = null, double? height = null) =>
        new(name, sex, born, height);

    // ── The id becomes a path, so it is a security boundary ─────────────────────

    [Theory]
    [InlineData("default")]
    [InlineData("p-3f9a01bc")]
    [InlineData("a")]
    [InlineData("0abc")]
    public void OrdinaryIdsAreAccepted(string id) => Assert.True(ProfileIds.IsValid(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("../secrets")]
    [InlineData("..\\secrets")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows")]
    [InlineData("x.db")]
    [InlineData("x y")]
    [InlineData("UPPER")]
    [InlineData("-leading")]
    [InlineData("a%2e%2e")]
    [InlineData("a\0b")]
    [InlineData("p-3f9a01bc\n")]     // .NET's $ also matches just before a final newline
    [InlineData("default\n")]
    public void AnythingThatCouldReachAnotherPathIsRefused(string? id) => Assert.False(ProfileIds.IsValid(id));

    [Fact]
    public void AnIdOverFortyCharactersIsRefused() =>
        Assert.False(ProfileIds.IsValid(new string('a', 41)));

    [Fact]
    public void GeneratedIdsAreValidOpaqueAndDifferent()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => ProfileIds.New()).ToList();

        Assert.All(ids, id => Assert.True(ProfileIds.IsValid(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // Opaque on purpose: a name is personal data and file names end up in logs.
        Assert.All(ids, id => Assert.StartsWith("p-", id));
    }

    [Fact]
    public void ThePathIsNeverBuiltFromAnInvalidId()
    {
        foreach (var bad in new[] { "../x", "a/b", "", "x.db", "C:\\x" })
            Assert.Throws<ArgumentException>(() => _paths.DatabaseFile(bad));
    }

    [Fact]
    public void EveryProfilePathStaysInsideTheProfilesDirectory()
    {
        var path = _paths.DatabaseFile("p-0011aabb");

        Assert.StartsWith(Path.GetFullPath(_paths.ProfilesDirectory), path);
        Assert.EndsWith("p-0011aabb.db", path);
    }

    [Fact]
    public void TheOriginalDatabaseKeepsItsOwnNameAndLocation()
    {
        // The reason this design is safe to deploy: the existing file is the default profile,
        // byte for byte, and is not renamed, moved or opened any differently.
        Assert.Equal(Path.Combine(_root, "vitara.db"), _paths.DatabaseFile(ProfileIds.Default));
    }

    // ── Which profile a request means ───────────────────────────────────────────

    [Fact]
    public void NoProfileMeansTheOriginalOne()
    {
        var r = ProfileResolver.Resolve(null, null, null, "/api/sleep");

        Assert.Equal(ProfileIds.Default, r.Id);
        Assert.Null(r.Error);
    }

    [Fact]
    public void TheHeaderWinsOverTheQueryString()
    {
        var r = ProfileResolver.Resolve("p-aaaa1111", "p-bbbb2222", null, "/api/sleep");

        Assert.Equal("p-aaaa1111", r.Id);
    }

    [Fact]
    public void TheOuraLinkNamesThePersonInTheQueryBecauseANavigationCannotCarryAHeader()
    {
        Assert.Equal("p-cccc3333", ProfileResolver.Resolve(null, "p-cccc3333", null, "/api/oura/auth").Id);
    }

    [Fact]
    public void ThePersonComesBackFromOuraInStateAndOnlyOnTheCallback()
    {
        // Oura hands the browser back exactly what it was sent. `state` is trusted as a
        // profile only on the one route that needs it: anywhere else a stray ?state= must
        // not be able to select somebody's data.
        Assert.Equal("p-dddd4444", ProfileResolver.Resolve(null, null, "p-dddd4444", "/api/oura/callback").Id);
        Assert.Equal(ProfileIds.Default, ProfileResolver.Resolve(null, null, "p-dddd4444", "/api/sleep").Id);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("x.db")]
    public void AnInvalidIdIsAnErrorAndNeverSilentlyTheDefault(string bad)
    {
        // Falling back to the default on a malformed id would show one person another
        // person's data and call it a success.
        var r = ProfileResolver.Resolve(bad, null, null, "/api/sleep");

        Assert.NotNull(r.Error);
    }

    // ── Creating and listing people ─────────────────────────────────────────────

    [Fact]
    public void TheListAlwaysStartsWithTheOriginalProfile()
    {
        var list = _catalog.List();

        Assert.Single(list);
        Assert.True(list[0].IsDefault);
    }

    [Fact]
    public async Task ACreatedPersonExistsAndCarriesTheirNameWithoutTheNameEverTouchingAPath()
    {
        var created = await _catalog.CreateAsync(Form("Priya Raman", "female", new DateOnly(1988, 4, 2), 1.62));

        Assert.True(_catalog.Exists(created.Id));
        Assert.Equal("Priya Raman", _catalog.List().First(p => p.Id == created.Id).Name);

        // The file is named after the opaque id, never the name.
        Assert.DoesNotContain("priya", Directory.GetFiles(_paths.ProfilesDirectory).Single().ToLowerInvariant());
    }

    [Fact]
    public async Task ListingPeopleLeavesNoStrayFilesBehind()
    {
        // EF Core creates SQLite databases in WAL mode. Opening one read-only to read a name
        // used to leave -wal and -shm files beside every profile, because a read-only
        // connection may not remove them. Found by a test that expected exactly one file.
        await _catalog.CreateAsync(Form("Priya"));

        _catalog.List();
        _catalog.List();

        Assert.Single(Directory.GetFiles(_paths.ProfilesDirectory));
    }

    [Fact]
    public async Task ANewDatabaseIsCompleteBeforeAnythingCanSeeIt()
    {
        var created = await _catalog.CreateAsync(Form());

        // No staging file is left behind, and nothing but the finished file is listed.
        Assert.Empty(Directory.GetFiles(_paths.ProfilesDirectory, "*.provisioning"));

        using var scope = Scope(created.Id, out var sp);
        var repo = scope.ServiceProvider.GetRequiredService<IVitaraRepository>();

        Assert.NotNull(await repo.GetProfileAsync());
        Assert.NotEmpty(await repo.GetReferenceRangesAsync());   // a first blood panel would otherwise read "no range"
    }

    [Fact]
    public async Task AHalfBuiltFileIsNeverListed()
    {
        Directory.CreateDirectory(_paths.ProfilesDirectory);
        File.WriteAllText(Path.Combine(_paths.ProfilesDirectory, "p-deadbeef.db.provisioning"), "x");

        Assert.Single(_catalog.List());
        Assert.False(_catalog.Exists("p-deadbeef"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreatingSomeoneNeverTouchesTheOriginalDatabase()
    {
        var original = _paths.DatabaseFile(ProfileIds.Default);
        File.WriteAllText(original, "pretend this is two years of someone's health data");
        var before = File.ReadAllBytes(original);

        await _catalog.CreateAsync(Form());

        Assert.Equal(before, File.ReadAllBytes(original));
    }

    // ── Isolation: the point of the design ──────────────────────────────────────

    [Fact]
    public async Task WhatOnePersonRecordsTheOtherCannotSee()
    {
        var asha = await _catalog.CreateAsync(Form("Asha"));
        var ravi = await _catalog.CreateAsync(Form("Ravi"));

        // What startup does on a real box: everybody's file, including the original, gets its
        // schema. Without it the default database would be an empty file with no tables.
        using (var startup = Provider()) await ProfileDatabases.EnsureAllAsync(startup);

        // Both weigh in on the SAME DAY. In a shared table keyed by date this is the collision
        // that ruled out a tenant column; in separate files it is two ordinary rows.
        var day = new DateOnly(2026, 10, 3);

        using (var scope = Scope(asha.Id, out _))
            await scope.ServiceProvider.GetRequiredService<IVitaraRepository>()
                .UpsertWeighInAsync(new WeighIn { Id = day.ToString("yyyy-MM-dd"), Day = day, WeightKg = 61.0 });

        using (var scope = Scope(ravi.Id, out _))
            await scope.ServiceProvider.GetRequiredService<IVitaraRepository>()
                .UpsertWeighInAsync(new WeighIn { Id = day.ToString("yyyy-MM-dd"), Day = day, WeightKg = 84.5 });

        using (var scope = Scope(asha.Id, out _))
        {
            var rows = await scope.ServiceProvider.GetRequiredService<IVitaraRepository>().GetWeighInsAsync(day, day);
            Assert.Equal(61.0, Assert.Single(rows).WeightKg);
        }

        using (var scope = Scope(ravi.Id, out _))
        {
            var rows = await scope.ServiceProvider.GetRequiredService<IVitaraRepository>().GetWeighInsAsync(day, day);
            Assert.Equal(84.5, Assert.Single(rows).WeightKg);
        }

        using (var scope = Scope(ProfileIds.Default, out _))
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IVitaraRepository>().GetWeighInsAsync(day, day));
    }

    [Fact]
    public async Task ASecondPersonsDataIsInADifferentFileNotAFilteredView()
    {
        var a = await _catalog.CreateAsync(Form("A"));
        var b = await _catalog.CreateAsync(Form("B"));

        Assert.NotEqual(_paths.DatabaseFile(a.Id), _paths.DatabaseFile(b.Id));
        Assert.True(File.Exists(_paths.DatabaseFile(a.Id)));
        Assert.True(File.Exists(_paths.DatabaseFile(b.Id)));
    }

    [Fact]
    public async Task EachWorkerIterationIsToldWhoItIsFor()
    {
        var a = await _catalog.CreateAsync(Form("A"));
        var seen = new List<string>();

        using var sp = Provider();

        await ProfileScopes.ForEachAsync(sp, (scope, id) =>
        {
            // The scope's context must already say who this is, before any database is resolved.
            seen.Add(scope.ServiceProvider.GetRequiredService<ProfileContext>().ProfileId + "|" + id);
            return Task.CompletedTask;
        });

        Assert.Equal([$"{ProfileIds.Default}|{ProfileIds.Default}", $"{a.Id}|{a.Id}"], seen);
    }

    [Fact]
    public async Task OnePersonsFailureDoesNotStopTheOthers()
    {
        // The sync worker hits an expired Oura token for one profile and still has to refresh
        // everybody else's. A loop that threw on the first problem would turn one person's
        // broken link into everyone's.
        await _catalog.CreateAsync(Form("A"));
        await _catalog.CreateAsync(Form("B"));

        var reached = 0;
        using var sp = Provider();

        await ProfileScopes.ForEachAsync(sp, (_, id) =>
        {
            reached++;
            if (reached == 1) throw new InvalidOperationException("the first profile is broken");
            return Task.CompletedTask;
        });

        Assert.Equal(3, reached);
    }

    // ── Deleting someone ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingAPersonDeletesTheirFileAndNobodyElses()
    {
        var keep = await _catalog.CreateAsync(Form("Keep"));
        var gone = await _catalog.CreateAsync(Form("Gone"));

        Assert.True(_catalog.Delete(gone.Id));

        Assert.False(File.Exists(_paths.DatabaseFile(gone.Id)));
        Assert.False(_catalog.Exists(gone.Id));
        Assert.True(File.Exists(_paths.DatabaseFile(keep.Id)));
    }

    [Fact]
    public void TheOriginalProfileCanNeverBeDeletedByTheCatalog()
    {
        File.WriteAllText(_paths.DatabaseFile(ProfileIds.Default), "x");

        Assert.False(_catalog.Delete(ProfileIds.Default));
        Assert.True(File.Exists(_paths.DatabaseFile(ProfileIds.Default)));
    }

    [Fact]
    public void DeletingSomeoneWhoDoesNotExistIsAFalseNotAnError() =>
        Assert.False(_catalog.Delete("p-00000000"));

    [Fact]
    public void ABadIdCannotBeDeleted() =>
        Assert.False(_catalog.Delete("../vitara"));

    // ── The controllers ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheControllerCreatesListsAndRefusesAnUnconfirmedDelete()
    {
        var ctrl = new ProfilesController(_catalog);

        var created = Assert.IsType<CreatedResult>(await ctrl.Create(new ProfileInput("Maya", "female", "1990-01-05", 165.0)));
        var id = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(created.Value, Opts)).GetProperty("id").GetString()!;

        Assert.Contains(id, JsonSerializer.Serialize(((OkObjectResult)ctrl.List()).Value, Opts));

        // Destructive and irreversible, so it has to be asked for twice.
        Assert.IsType<BadRequestObjectResult>(ctrl.Delete(id, confirm: null));
        Assert.IsType<BadRequestObjectResult>(ctrl.Delete(id, confirm: "something-else"));
        Assert.True(_catalog.Exists(id));

        Assert.IsType<NoContentResult>(ctrl.Delete(id, confirm: id));
        Assert.False(_catalog.Exists(id));
    }

    [Fact]
    public void TheOriginalProfileCannotBeDeletedThroughTheApiEither()
    {
        var ctrl = new ProfilesController(_catalog);

        Assert.IsType<BadRequestObjectResult>(ctrl.Delete(ProfileIds.Default, confirm: ProfileIds.Default));
    }

    [Fact]
    public async Task AnInvalidPersonIsRejectedBeforeAnyFileIsMade()
    {
        var ctrl = new ProfilesController(_catalog);

        Assert.IsType<BadRequestObjectResult>(await ctrl.Create(new ProfileInput("", null, null, null)));       // no name
        Assert.IsType<BadRequestObjectResult>(await ctrl.Create(new ProfileInput("X", "other", null, null)));   // sex
        Assert.IsType<BadRequestObjectResult>(await ctrl.Create(new ProfileInput("X", null, null, 5.5)));       // feet typed as cm

        Assert.Single(_catalog.List());
    }

    // ── Existing databases survive the new columns ──────────────────────────────

    [Fact]
    public async Task ADatabaseFromBeforeProfilesExistedGainsTheColumnsAndKeepsItsData()
    {
        // The deploy-time case that matters: the live database has a Profiles table with a
        // height in it and none of the new columns. It must come out with all of them and the
        // same height, and a failure here would be on the only copy of the data.
        var file = _paths.DatabaseFile(ProfileIds.Default);

        await using (var old = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            await old.OpenAsync();
            await using var cmd = old.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Profiles (
                    Id TEXT PRIMARY KEY, Age INTEGER, Weight REAL, Height REAL,
                    BiologicalSex TEXT, Email TEXT,
                    UpdatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00');
                INSERT INTO Profiles (Id, Age, Height, BiologicalSex) VALUES ('default', 44, 1.651, 'male');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        using var sp = Provider();
        await ProfileDatabases.EnsureAllAsync(sp);

        using var scope = Scope(ProfileIds.Default, out _);
        var profile = await scope.ServiceProvider.GetRequiredService<IVitaraRepository>().GetProfileAsync();

        Assert.NotNull(profile);
        Assert.Equal(1.651, profile!.Height);
        Assert.Equal("male", profile.BiologicalSex);
        Assert.Equal(44, profile.Age);
        Assert.Null(profile.Name);
        Assert.Null(profile.LockedFields);
    }

    [Fact]
    public async Task StartingUpTwiceChangesNothing()
    {
        await _catalog.CreateAsync(Form("A"));
        using var sp = Provider();

        await ProfileDatabases.EnsureAllAsync(sp);
        await ProfileDatabases.EnsureAllAsync(sp);

        Assert.Equal(2, _catalog.List().Count);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddVitaraProfiles(_paths);
        services.AddScoped<IVitaraRepository, VitaraRepository>();
        return services.BuildServiceProvider();
    }

    // The scope is told who it is for BEFORE the repository is resolved, which is the rule
    // the real request pipeline follows.
    private IServiceScope Scope(string profileId, out ServiceProvider sp)
    {
        sp = Provider();
        var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProfileContext>().Use(profileId);
        return scope;
    }
}
