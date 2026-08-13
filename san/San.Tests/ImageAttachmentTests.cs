using San.Application;

namespace San.Tests;

// The limits here are a backstop — the browser downscales before upload — but they are
// the only thing standing between a client that doesn't and a context window with no
// room left for San's own tool catalogue.
public class ImageAttachmentTests
{
    // A 1x1 JPEG is enough: validation looks at the header and the encoding, not pixels.
    private const string TinyJpeg =
        "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/APn+iiigD//Z";

    [Fact]
    public void AbsentImageIsValid()
    {
        Assert.True(ImageAttachment.TryValidate(null, out var err));
        Assert.Null(err);
        Assert.True(ImageAttachment.TryValidate("", out _));
    }

    [Fact]
    public void AcceptsARealJpegDataUrl()
    {
        Assert.True(ImageAttachment.TryValidate(TinyJpeg, out var err));
        Assert.Null(err);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("image/gif")]
    public void AcceptsEverySupportedMediaType(string mediaType)
        => Assert.True(ImageAttachment.TryValidate($"data:{mediaType};base64,AAAA", out _));

    // Formats llama.cpp's vision path can't decode must be refused here, where the user
    // gets a sentence explaining it, rather than at the model with an opaque 400.
    [Theory]
    [InlineData("image/tiff")]
    [InlineData("image/svg+xml")]
    [InlineData("image/heic")]
    public void RejectsUnsupportedImageTypes(string mediaType)
    {
        Assert.False(ImageAttachment.TryValidate($"data:{mediaType};base64,AAAA", out var err));
        Assert.Contains(mediaType, err);
    }

    // An SVG is markup, not pixels; a PDF is not an image at all. Both are things a file
    // picker will happily hand over.
    [Theory]
    [InlineData("data:application/pdf;base64,AAAA")]
    [InlineData("data:text/html;base64,AAAA")]
    [InlineData("https://example.com/cat.jpg")]
    [InlineData("just some text")]
    [InlineData("data:image/jpeg,notbase64")]
    public void RejectsAnythingThatIsNotABase64ImageDataUrl(string raw)
    {
        Assert.False(ImageAttachment.TryValidate(raw, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void RejectsAnOversizedImage()
    {
        var huge = "data:image/jpeg;base64," + new string('A', 4_000_001);
        Assert.False(ImageAttachment.TryValidate(huge, out var err));
        Assert.Contains("too large", err);
    }

    // History records that a picture was sent, never the picture — persisting base64
    // would grow san.db without bound and make ChatWindow replay the image on every
    // later turn.
    [Fact]
    public void HistoryKeepsTheWordsAndNotesTheImage()
    {
        var stored = ImageAttachment.DescribeForHistory("what is this receipt for?");
        Assert.Contains("what is this receipt for?", stored);
        Assert.Contains(ImageAttachment.Marker, stored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ImageWithNoWordsIsJustTheMarker(string content)
        => Assert.Equal(ImageAttachment.Marker, ImageAttachment.DescribeForHistory(content));
}
