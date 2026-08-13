namespace Maaya.Auth;

// Reading the auth secrets out of the environment, and surviving the one way they
// reliably get mangled on the way in.
//
// A bcrypt hash is full of '$'. Compose treats '$' as special INSIDE the compose file,
// where '$$' is the escape for a literal '$' — so a hash pasted into an `environment:`
// block has to be written doubled. Values listed under `env_file:` get no such
// treatment: they arrive byte-for-byte, doubles and all. Move a hash from one to the
// other (or follow a comment written for the other) and the container ends up with
// "$$2a$$12$$..." — 63 characters, not a bcrypt hash, and every password is wrong
// forever with no symptom beyond "invalid credentials".
//
// Rather than depend on an env file being written exactly right, undo the doubling
// when — and only when — that is what turns an unusable string into a well-formed
// hash. A correct hash is returned untouched, and a genuinely wrong value stays wrong
// rather than being silently "repaired" into something else.
public static class AuthSecrets
{
    // bcrypt output is invariably 60 chars beginning "$2" ($2a$/$2b$/$2y$).
    public static bool LooksLikeBcrypt(string? hash) =>
        hash is { Length: 60 } && hash.StartsWith("$2", StringComparison.Ordinal);

    public static string PasswordHash() => Normalize(Environment.GetEnvironmentVariable("AUTH_PASSWORD_HASH") ?? "");

    // Exposed for the startup diagnostic, which reports whether the raw value needed
    // this — "your env file has doubled dollars" is a far more useful thing to read in
    // the log than "login failed".
    public static bool NeededUndoubling(string raw) => !LooksLikeBcrypt(raw) && LooksLikeBcrypt(raw.Replace("$$", "$"));

    private static string Normalize(string raw)
    {
        if (LooksLikeBcrypt(raw)) return raw;
        var collapsed = raw.Replace("$$", "$");
        return LooksLikeBcrypt(collapsed) ? collapsed : raw;
    }
}
