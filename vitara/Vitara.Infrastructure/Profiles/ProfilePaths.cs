namespace Vitara.Infrastructure.Profiles;

// Where each person's database lives.
//
//   <root>/vitara.db              the original, now the profile called "default"
//   <root>/profiles/<id>.db       everyone else, one file each
//
// ONE FILE PER PERSON is the whole isolation story. A tenant column on every table was
// the obvious design and was costed and rejected: eleven of the tables are keyed by the
// DATE ITSELF (a weigh-in's primary key is "2026-10-03"), so two people weighing in on the
// same day would collide, and every unique index would need rebuilding on the only copy of
// the data. Separate files need none of that, cannot leak between people because there is
// no shared table to forget a filter on, and turn "delete everything about me" into
// deleting a file.
public class ProfilePaths(string root)
{
    public string Root { get; } = root;

    public string ProfilesDirectory => Path.Combine(Root, "profiles");

    // Beside the process's working directory, where the single database has always been.
    public static ProfilePaths FromWorkingDirectory() =>
        new(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..")));

    public string DatabaseFile(string id)
    {
        if (!ProfileIds.IsValid(id))
            throw new ArgumentException("Not a valid profile id.", nameof(id));

        if (id == ProfileIds.Default) return Path.Combine(Root, "vitara.db");

        var path = Path.GetFullPath(Path.Combine(ProfilesDirectory, id + ".db"));

        // Belt and braces. The id has already been matched against a strict pattern; this
        // checks the RESULT, so a future change to that pattern cannot quietly open a path
        // outside the profiles directory.
        var directory = Path.GetFullPath(ProfilesDirectory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(directory, StringComparison.Ordinal))
            throw new InvalidOperationException("Profile path escaped the profiles directory.");

        return path;
    }
}
