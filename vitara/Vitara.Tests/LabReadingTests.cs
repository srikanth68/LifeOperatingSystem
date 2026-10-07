using Vitara.Insight.Health;

namespace Vitara.Tests;

// The numbers behind a lab sentence, as a picture needs them.
//
// What matters here is the refusals. A gauge drawn from a half-understood record is worse than
// no gauge: it looks like data, and nobody can tell it was guessed.
public class LabReadingTests
{
    // The shape the detector actually writes.
    private const string Stored =
        """{"value":24,"previous":null,"drawnOn":"2026-09-02","previousDrawnOn":null,"range":{"Low":30,"High":100,"Unit":"ng/mL","Sex":null,"LabName":null},"standing":"below"}""";

    [Fact]
    public void The_detectors_own_evidence_is_read_back_exactly()
    {
        var r = LabReading.FromEvidence(Stored)!;

        Assert.Equal(24, r.Value);
        Assert.Null(r.Previous);
        Assert.Equal(30, r.Low);
        Assert.Equal(100, r.High);
        Assert.Equal("ng/mL", r.Unit);
        Assert.Equal("2026-09-02", r.DrawnOn);
        Assert.Equal("below", r.Standing);
    }

    [Fact]
    public void A_range_with_only_an_upper_bound_is_kept_and_the_missing_side_stays_missing()
    {
        // LDL has a ceiling and no floor. Inventing a floor of zero would draw a green zone
        // that starts somewhere the lab never said.
        var r = LabReading.FromEvidence(
            """{"value":121,"previous":104,"range":{"Low":null,"High":100,"Unit":"mg/dL"},"standing":"above"}""")!;

        Assert.Null(r.Low);
        Assert.Equal(100, r.High);
        Assert.Equal(104, r.Previous);
    }

    [Fact]
    public void Camel_case_in_the_range_is_accepted_too() =>
        Assert.Equal(30, LabReading.FromEvidence("""{"value":24,"range":{"low":30,"high":100}}""")!.Low);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"previous":3,"range":{"Low":1,"High":2}}""")]                  // no value
    [InlineData("""{"value":"high","range":{"Low":1,"High":2}}""")]                // value is not a number
    [InlineData("""{"value":24}""")]                                               // no range at all
    [InlineData("""{"value":24,"range":{"Low":null,"High":null}}""")]              // a range with no bounds
    [InlineData("""{"value":24,"range":"30-100"}""")]                              // range is not an object
    public void Anything_that_cannot_be_drawn_honestly_is_not_drawn(string? json) =>
        Assert.Null(LabReading.FromEvidence(json));
}
