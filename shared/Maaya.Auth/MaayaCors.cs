namespace Maaya.Auth;

// Merges each module's default dev origins with deployment-specific ones from
// CORS_ORIGINS (comma-separated), so containers serving the frontend from another
// host (e.g. http://100.126.41.41:3000 on Everest) work without code changes.
public static class MaayaCors
{
    public static string[] Origins(params string[] defaults)
    {
        var extra = (Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return defaults.Concat(extra).Distinct().ToArray();
    }
}
