namespace Vitara.Infrastructure.Profiles;

// Whose data this request, or this unit of background work, is about.
//
// Scoped, so every request and every worker iteration has its own. It must be set BEFORE
// anything that touches the database is resolved from the same scope: the database
// connection is chosen when the context is created, not when it is first queried, so
// choosing a profile after the repository exists would quietly read the default one.
//
// Absent means "default", which is exactly how every existing client already behaves --
// the phone app, the assistant's tools and the web app before it had a switcher all send
// no profile and keep getting the original database.
public class ProfileContext
{
    public string ProfileId { get; private set; } = ProfileIds.Default;

    public void Use(string id)
    {
        if (!ProfileIds.IsValid(id))
            throw new ArgumentException("Not a valid profile id.", nameof(id));

        ProfileId = id;
    }
}

// Working out which profile a request means, from the three places it can arrive.
//
//   X-Profile-Id header   how the web app says it
//   ?profile=             for the one request a browser makes by NAVIGATING, which cannot
//                         carry a header: the Oura link, opened in a new tab
//   ?state=               how that same flow comes BACK. Oura redirects the browser to our
//                         callback and hands back only what we put in `state`, so the
//                         profile has to ride there or the token would be filed under the
//                         default person
//
// Pure, so it can be tested without a web server.
public static class ProfileResolver
{
    public record Result(string Id, string? Error);

    public static Result Resolve(string? header, string? query, string? state, string path)
    {
        var isOuraCallback = path.EndsWith("/api/oura/callback", StringComparison.OrdinalIgnoreCase);

        var raw = FirstNonEmpty(header, query, isOuraCallback ? state : null);
        if (raw is null) return new Result(ProfileIds.Default, null);

        return ProfileIds.IsValid(raw)
            ? new Result(raw, null)
            : new Result(ProfileIds.Default, "That is not a valid profile id.");
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
