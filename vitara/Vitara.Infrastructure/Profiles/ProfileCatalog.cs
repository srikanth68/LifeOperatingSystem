using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Data;

namespace Vitara.Infrastructure.Profiles;

public record ProfileSummary(string Id, string? Name, bool IsDefault);

// The list of people, and making and removing them.
//
// THE FILE SYSTEM IS THE REGISTRY. There is no second table that could disagree with
// reality about who exists: a person exists if their database file does. That removes a
// whole class of "the list says they are there and the data is gone" bugs, and it means
// the three processes that share this directory -- the API, the sync worker and the
// analysis worker -- all see the same answer without telling each other anything.
public class ProfileCatalog(ProfilePaths paths)
{
    // The original database is always first and always present in the list, even on a
    // fresh install before its file exists: it is where an unscoped request has always gone.
    public IReadOnlyList<ProfileSummary> List()
    {
        var people = new List<ProfileSummary> { Summarise(ProfileIds.Default) };

        if (Directory.Exists(paths.ProfilesDirectory))
            foreach (var file in Directory.EnumerateFiles(paths.ProfilesDirectory, "*.db").OrderBy(File.GetCreationTimeUtc))
            {
                var id = Path.GetFileNameWithoutExtension(file);
                if (ProfileIds.IsValid(id) && id != ProfileIds.Default) people.Add(Summarise(id));
            }

        return people;
    }

    public bool Exists(string id) =>
        ProfileIds.IsValid(id) && (id == ProfileIds.Default || File.Exists(paths.DatabaseFile(id)));

    // Built in a staging file and moved into place only when it is complete.
    //
    // The catalogue lists anything that ends in .db, and the analysis worker opens whatever
    // the catalogue lists. A file that appeared the instant creation began, with half its
    // tables, would be listed and queried mid-way. Renaming is atomic: until it happens
    // the person does not exist as far as anything else can tell, and after it they are
    // whole.
    public async Task<ProfileSummary> CreateAsync(ProfileForm form)
    {
        Directory.CreateDirectory(paths.ProfilesDirectory);

        string id;
        do id = ProfileIds.New(); while (File.Exists(paths.DatabaseFile(id)));

        var final = paths.DatabaseFile(id);
        var staging = final + ".provisioning";

        try
        {
            // Pooling off, so nothing holds the staging file open when it is renamed -- on
            // Windows a pooled handle makes the move fail, and it is the sort of failure
            // that only shows up on a developer machine.
            var options = new DbContextOptionsBuilder<VitaraDbContext>()
                .UseSqlite($"Data Source={staging};Pooling=False")
                .Options;

            await using (var db = new VitaraDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                await VitaraDbContext.CreateMissingTablesAsync(db);

                var repo = new VitaraRepository(db);

                // Every database carries its own reference ranges. Without them a new
                // person's first blood panel would read as "no range recorded".
                await repo.SeedReferenceRangesAsync(ReferenceRanges.Seed);
                await repo.SaveProfileAsync(ProfileMerge.FromForm(null, form));
            }

            // EF Core creates SQLite databases in WAL mode, so a database can have -wal and -shm
            // companions. They are normally gone once the last connection closes, but a WAL
            // that is moved without its database -- or a database moved without its WAL --
            // loses whatever had not been checkpointed. Move whatever is there, sidecars first,
            // and the database LAST: only the .db is ever listed, so the person still does not
            // exist until the final rename.
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(staging + suffix)) File.Move(staging + suffix, final + suffix);

            File.Move(staging, final);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }

        return new ProfileSummary(id, string.IsNullOrWhiteSpace(form.Name) ? null : form.Name.Trim(), false);
    }

    // Deleting a person is deleting a file, which is the point of the design: there is no
    // table to sweep, nothing in a backup of some OTHER person's file, and nothing left to
    // forget. The original database is refused here as well as in the controller, because
    // "delete the default" is the one request that must never succeed by accident.
    public bool Delete(string id)
    {
        if (id == ProfileIds.Default || !ProfileIds.IsValid(id)) return false;

        var file = paths.DatabaseFile(id);
        if (!File.Exists(file)) return false;

        // Releases any pooled handle on the file, or the delete fails on Windows and
        // leaves it half-removed on a platform that lets you unlink an open file.
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { file, file + "-wal", file + "-shm", file + "-journal" })
            TryDelete(path);

        return true;
    }

    private ProfileSummary Summarise(string id) =>
        new(id, ReadName(id), id == ProfileIds.Default);

    // Straight to the file with a read-only, unpooled connection: listing people must not
    // construct an entity model per file, and must not leave a handle open on any of them.
    private string? ReadName(string id)
    {
        var file = paths.DatabaseFile(id);
        if (!File.Exists(file)) return null;

        try
        {
            // ReadWrite rather than ReadOnly, though this only SELECTs. In WAL mode even a
            // read-only open has to create -wal and -shm files, and a read-only connection is
            // not allowed to remove them again, so merely LISTING people left two stray files
            // beside every database. A read-write connection cleans up after itself on close.
            using var conn = new SqliteConnection($"Data Source={file};Mode=ReadWrite;Pooling=False");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Profiles LIMIT 1";

            return cmd.ExecuteScalar() as string;
        }
        catch (SqliteException)
        {
            // A database from before names existed has no such column. Not an error: it is
            // simply a person who has not been given a name yet.
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* a leftover staging file is harmless; it is never listed */ }
    }
}
