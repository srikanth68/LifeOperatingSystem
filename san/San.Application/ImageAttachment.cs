using System.Text.RegularExpressions;

namespace San.Application;

// What San will accept as an attached picture, and what it records in place of one.
//
// The browser already downscales before upload (SanModule.attachImage), so these
// limits are a backstop against a client that doesn't — not the primary control. They
// matter because an oversized image doesn't fail cleanly: it's silently expensive.
// Gemma turns a picture into vision tokens that share the same 32K context as the
// system prompt, the module snapshot and the whole tool catalogue, so a big enough
// image evicts San's knowledge of what it can do in order to look at a photo.
public static partial class ImageAttachment
{
    // Formats llama.cpp's vision path decodes. GIF is accepted and treated as a still —
    // the model sees the first frame, which is the honest behaviour rather than a
    // refusal the user has to guess at.
    private static readonly string[] Allowed = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    // ~4 MB of base64, roughly a 3 MB image. Comfortably above anything the client
    // produces after downscaling, and far below what would blow the context window.
    private const int MaxDataUrlChars = 4_000_000;

    // Stored in chat history in place of the image itself. Keeps the transcript honest
    // about what was sent without persisting megabytes into SQLite or re-sending the
    // picture on every later turn.
    public const string Marker = "[image attached]";

    public static bool TryValidate(string? dataUrl, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(dataUrl)) return true;   // absent is fine

        if (dataUrl.Length > MaxDataUrlChars)
        {
            error = $"That image is too large ({dataUrl.Length / 1_000_000.0:0.0} MB encoded). Please send a smaller one.";
            return false;
        }

        var m = DataUrlRegex().Match(dataUrl);
        if (!m.Success)
        {
            error = "That doesn't look like an image San can read.";
            return false;
        }

        var mediaType = m.Groups[1].Value.ToLowerInvariant();
        if (!Allowed.Contains(mediaType))
        {
            error = $"{mediaType} isn't supported — send a JPEG, PNG or WebP.";
            return false;
        }

        return true;
    }

    // What gets written to the transcript: the user's words plus a marker, or just the
    // marker when they sent a picture and said nothing.
    public static string DescribeForHistory(string content) =>
        string.IsNullOrWhiteSpace(content) ? Marker : $"{content}\n\n{Marker}";

    [GeneratedRegex(@"^data:(image/[a-zA-Z0-9+.-]+);base64,[A-Za-z0-9+/]+=*$", RegexOptions.Compiled)]
    private static partial Regex DataUrlRegex();
}
