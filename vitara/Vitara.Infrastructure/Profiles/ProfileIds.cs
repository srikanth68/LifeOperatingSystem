using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Vitara.Infrastructure.Profiles;

// Which person, as a string that is safe to put in a file name.
//
// THE ID BECOMES A PATH, so this is a security boundary and not a formatting rule. A
// profile's data lives in a file named after its id, and the id arrives from the browser
// in a request header. "../../etc/something" in that header must be unable to reach
// anything, so ids are lowercase letters, digits and hyphens, at most forty characters,
// starting with a letter or digit -- and anything else is refused before it is ever
// concatenated into a path.
//
// Generated ids are opaque on purpose ("p-3f9a01bc", not a slug of the person's name).
// A name is personal data and file names end up in logs, backups, directory listings and
// error messages.
public static class ProfileIds
{
    // The original database, from before there were profiles. It keeps its name and its
    // file, byte for byte, which is what makes this change safe to deploy.
    public const string Default = "default";

    // \z, not $: in .NET `$` also matches just before a trailing newline, which would let
    // an id with a newline on the end through a check whose whole job is to be strict.
    private static readonly Regex Valid = new(@"^[a-z0-9][a-z0-9-]{0,39}\z", RegexOptions.Compiled);

    public static bool IsValid(string? id) => id is not null && Valid.IsMatch(id);

    public static string New() => "p-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}
