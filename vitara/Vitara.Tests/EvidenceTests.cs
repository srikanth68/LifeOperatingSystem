using System.Reflection;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// The grades are a promise that the system will not let an experimental number sit in
// the same visual weight as an established one.
//
// A promise like that is kept by tests or it is not kept at all: the failure mode is
// not somebody writing a wrong grade, it is somebody adding a derived score and simply
// not thinking about the grade, after which it renders ungraded and reads as fact.
public class EvidenceTests
{
    private static List<string> ConceptConstants() => typeof(Evidence)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    [Fact]
    public void EveryComputedMetricIsGraded()
    {
        // A measured step count needs no grade -- nobody disputes what a step is. A
        // number this system worked out is a claim, and every claim carries one.
        var ungraded = MetricCatalogue.All
            .Where(m => m.Computed && Evidence.For(m.Key) is null)
            .Select(m => m.Key)
            .ToList();

        Assert.Empty(ungraded);
    }

    [Fact]
    public void EveryBloodResultIsGraded()
    {
        var ungraded = MetricCatalogue.All
            .Where(m => m.Group == MetricCatalogue.GroupLabs && Evidence.For(m.Key) is null)
            .Select(m => m.Key)
            .ToList();

        Assert.Empty(ungraded);
    }

    [Fact]
    public void EveryNamedConceptIsGraded()
    {
        // The forecast, the illness detector and biological age are not metrics, and
        // they are exactly the claims most in need of a grade.
        var ungraded = ConceptConstants().Where(c => Evidence.For(c) is null).ToList();

        Assert.Empty(ungraded);
    }

    [Fact]
    public void NoGradeIsAttachedToSomethingThatDoesNotExist()
    {
        // Catches the slow rot: a metric gets renamed, its grade keeps resolving to
        // nothing, and the UI quietly stops showing one.
        var concepts = ConceptConstants().ToHashSet();

        var orphans = Evidence.All
            .Where(n => MetricCatalogue.Find(n.Key) is null && !concepts.Contains(n.Key))
            .Select(n => n.Key)
            .ToList();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryGradeCarriesItsCaveat()
    {
        foreach (var note in Evidence.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(note.Claim), $"{note.Key} has no claim");
            Assert.False(string.IsNullOrWhiteSpace(note.Basis), $"{note.Key} has no basis");
            Assert.False(string.IsNullOrWhiteSpace(note.Caveat), $"{note.Key} has no caveat");

            // A caveat restating the claim is the shape of a caveat written to satisfy
            // a check rather than to limit anything.
            Assert.NotEqual(note.Claim, note.Caveat);
        }
    }

    [Fact]
    public void NothingIsGradedTwice()
    {
        var duplicates = Evidence.All
            .GroupBy(n => n.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    // ── The business rule ───────────────────────────────────────────────────────

    [Fact]
    public void EmergingAndExperimentalClaimsMustBeQualified()
    {
        foreach (var note in Evidence.All)
            Assert.Equal(note.Grade is Grade.C or Grade.D, note.MustQualify);
    }

    [Fact]
    public void BiologicalAgeIsNeverPresentedAsEstablished()
    {
        // The single rule the spec states outright: an emerging longevity concept must
        // never be shown as an established medical fact. Age-equivalents are the most
        // tempting one in the whole category, because they read like a diagnosis and
        // there is no agreed way to compute one.
        foreach (var key in new[] { Evidence.BiologicalAge, MetricKeys.CardiovascularAge })
        {
            var note = Evidence.For(key);
            Assert.NotNull(note);
            Assert.Equal(Grade.D, note!.Grade);
            Assert.True(note.MustQualify);
        }
    }

    [Fact]
    public void AVendorScoreIsNotGradedAboveItsParts()
    {
        // A readiness score built from HRV and resting heart rate cannot be better
        // evidenced than the things it is built from, and the weighting is undisclosed
        // on top. If this ever inverts, somebody has graded the convenience of a number
        // rather than the evidence for it.
        var readiness = Evidence.GradeFor(MetricKeys.ReadinessScore);
        var hrv = Evidence.GradeFor(MetricKeys.HrvRmssd);

        Assert.NotNull(readiness);
        Assert.NotNull(hrv);
        Assert.True(readiness > hrv, "a summary score outranks its own inputs");
    }

    [Fact]
    public void TheLabelsReadAsEnglishRatherThanAsLetters()
    {
        // The grade is shown to a person. "C" means nothing to anybody; "emerging"
        // means what it says.
        Assert.Equal("established", Evidence.Label(Grade.A));
        Assert.Equal("emerging", Evidence.Label(Grade.C));
        Assert.Equal("experimental", Evidence.Label(Grade.D));

        foreach (var note in Evidence.All)
            Assert.False(string.IsNullOrWhiteSpace(note.Label));
    }

    [Fact]
    public void TheMostEstablishedThingsAreTheOnesWithOutcomeEvidence()
    {
        // A sanity check on the table itself: the things trials have been run on sit
        // at A, and nothing a device maker invented does.
        Assert.Equal(Grade.A, Evidence.GradeFor(MetricKeys.Ldl));
        Assert.Equal(Grade.A, Evidence.GradeFor(MetricKeys.SystolicBp));
        Assert.Equal(Grade.A, Evidence.GradeFor(MetricKeys.Hba1c));

        Assert.False(Evidence.MustQualify(MetricKeys.Ldl));
        Assert.True(Evidence.MustQualify(Evidence.Forecast));
    }
}
