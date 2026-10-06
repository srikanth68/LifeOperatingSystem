using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Vitara.API.Controllers;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Profiles;

namespace Vitara.Tests;

// Who owns each field of a profile: the person, or the nightly sync.
//
// The bug these pin down was found by a person looking for a place to type their height.
// There was no such place, the sync wrote its whole answer over the row every night, and
// Oura returns nothing for a height nobody entered in the Oura app -- so even a height set
// through the one other route would have been erased the first night after.
public class ProfileMergeTests
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static UserProfile FromOura(int? age = 44, double? height = null, string? sex = "male") =>
        new() { Id = "default", Age = age, Height = height, BiologicalSex = sex, Weight = 80, Email = "a@example.com" };

    // ── The bug ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AHeightTypedInSurvivesTheNextNightsSync()
    {
        // The exact scenario. The person types a height; Oura has none; the sync runs.
        var typed = ProfileMerge.FromForm(null, new ProfileForm("Me", "male", null, 1.651));

        var afterSync = ProfileMerge.FromSync(typed, FromOura(height: null));

        Assert.Equal(1.651, afterSync.Height);
    }

    [Fact]
    public void EvenWhenOuraHasADifferentHeightTheOneYouTypedWins()
    {
        var typed = ProfileMerge.FromForm(null, new ProfileForm(null, null, null, 1.651));

        var afterSync = ProfileMerge.FromSync(typed, FromOura(height: 1.70));

        Assert.Equal(1.651, afterSync.Height);
    }

    [Fact]
    public void TheSyncNeverWritesAnEmptyValueOverARealOne()
    {
        // "Oura does not know" is not "this should now be empty".
        var existing = new UserProfile { Id = "default", Age = 44, Height = 1.65, BiologicalSex = "male", Email = "keep@example.com" };

        var after = ProfileMerge.FromSync(existing, new UserProfile { Id = "default" });

        Assert.Equal(44, after.Age);
        Assert.Equal(1.65, after.Height);
        Assert.Equal("male", after.BiologicalSex);
        Assert.Equal("keep@example.com", after.Email);
    }

    [Fact]
    public void AFieldNobodyTouchedIsStillTheSyncsToKeepCurrent()
    {
        // Locking is per field. Typing a height must not freeze everything else.
        var typed = ProfileMerge.FromForm(null, new ProfileForm(null, null, null, 1.651));

        var afterSync = ProfileMerge.FromSync(typed, FromOura(age: 45, sex: "female"));

        Assert.Equal(45, afterSync.Age);
        Assert.Equal("female", afterSync.BiologicalSex);
        Assert.Equal(80, afterSync.Weight);
    }

    [Fact]
    public void ANewProfileTakesWhateverTheSyncHas()
    {
        var after = ProfileMerge.FromSync(null, FromOura(height: 1.72));

        Assert.Equal(1.72, after.Height);
        Assert.Equal(44, after.Age);
    }

    // ── The form ────────────────────────────────────────────────────────────────

    [Fact]
    public void FillingInAFieldLocksItAndClearingItHandsItBack()
    {
        var typed = ProfileMerge.FromForm(null, new ProfileForm("Me", "male", null, 1.651));
        Assert.Contains(ProfileMerge.Height, ProfileMerge.Locked(typed));
        Assert.Contains(ProfileMerge.Sex, ProfileMerge.Locked(typed));

        // Emptying the height says "I have nothing to say here", so Oura may fill it again.
        var cleared = ProfileMerge.FromForm(typed, new ProfileForm("Me", "male", null, null));

        Assert.DoesNotContain(ProfileMerge.Height, ProfileMerge.Locked(cleared));
        Assert.Contains(ProfileMerge.Sex, ProfileMerge.Locked(cleared));
        Assert.Equal(1.72, ProfileMerge.FromSync(cleared, FromOura(height: 1.72)).Height);
    }

    [Fact]
    public void TheFormDoesNotDisturbWhatItDoesNotShow()
    {
        var existing = new UserProfile { Id = "default", Weight = 80, Email = "a@example.com", Age = 44 };

        var after = ProfileMerge.FromForm(existing, new ProfileForm("Me", null, null, null));

        Assert.Equal(80, after.Weight);
        Assert.Equal("a@example.com", after.Email);
    }

    [Fact]
    public void ASexIsStoredLowercaseWhateverWasTyped() =>
        Assert.Equal("female", ProfileMerge.FromForm(null, new ProfileForm(null, " Female ", null, null)).BiologicalSex);

    [Fact]
    public void ANameIsTrimmedAndABlankOneIsNoName()
    {
        Assert.Equal("Asha", ProfileMerge.FromForm(null, new ProfileForm("  Asha ", null, null, null)).Name);
        Assert.Null(ProfileMerge.FromForm(null, new ProfileForm("   ", null, null, null)).Name);
    }

    [Fact]
    public void MergingNeverMutatesTheRowItWasGiven()
    {
        // A merge that edited its input in place would change what the database context is
        // tracking before anything decided to save it.
        var existing = new UserProfile { Id = "default", Height = 1.60 };

        ProfileMerge.FromForm(existing, new ProfileForm("X", "male", null, 1.80));

        Assert.Equal(1.60, existing.Height);
        Assert.Null(existing.LockedFields);
    }

    // ── Age comes from a date of birth when there is one ────────────────────────

    [Fact]
    public void AgeIsWorkedOutFromADateOfBirthNotStoredAndLeftToGoStale()
    {
        var p = new UserProfile { DateOfBirth = new DateOnly(1985, 6, 15), Age = 99 };

        Assert.Equal(41, p.AgeOn(new DateOnly(2026, 10, 3)));
    }

    [Fact]
    public void ABirthdayThatHasNotHappenedYetThisYearIsNotCounted()
    {
        var p = new UserProfile { DateOfBirth = new DateOnly(1985, 6, 15) };

        Assert.Equal(40, p.AgeOn(new DateOnly(2026, 6, 14)));
        Assert.Equal(41, p.AgeOn(new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void ALeapDayBirthdayIsHandled()
    {
        var p = new UserProfile { DateOfBirth = new DateOnly(2000, 2, 29) };

        // Jurisdictions differ on whether a leap-day birthday falls on 28 Feb or 1 March in a
        // year without one. This follows DateOnly.AddYears, which clamps to 28 Feb; the
        // difference is one day in four years and changes no range or equation.
        Assert.Equal(25, p.AgeOn(new DateOnly(2026, 2, 27)));
        Assert.Equal(26, p.AgeOn(new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void WithoutADateOfBirthTheStoredAgeIsUsed() =>
        Assert.Equal(44, new UserProfile { Age = 44 }.AgeOn(new DateOnly(2026, 10, 3)));

    [Fact]
    public void ADateOfBirthInTheFutureFallsBackRatherThanReturningANegativeAge() =>
        Assert.Equal(44, new UserProfile { DateOfBirth = new DateOnly(2030, 1, 1), Age = 44 }.AgeOn(new DateOnly(2026, 10, 3)));

    [Fact]
    public void EnteringADateOfBirthReplacesAStaleSyncedAge()
    {
        var existing = new UserProfile { Id = "default", Age = 30 };

        var after = ProfileMerge.FromForm(existing, new ProfileForm(null, null, new DateOnly(1985, 6, 15), null));

        Assert.NotEqual(30, after.CurrentAge);
        Assert.Equal(after.AgeOn(LocalTime.Today), after.CurrentAge);
    }

    // ── What the form may contain ───────────────────────────────────────────────

    [Fact]
    public void FeetTypedAsCentimetresIsRejectedNotStoredAsFiveCentimetres()
    {
        var (form, error) = ProfileValidation.Validate(new ProfileInput(null, null, null, 5.5), nameRequired: false);

        Assert.Null(form);
        Assert.Contains("100 and 250", error);
    }

    [Fact]
    public void AStrayDigitInAHeightIsRejected() =>
        Assert.Null(ProfileValidation.Validate(new ProfileInput(null, null, null, 1650), nameRequired: false).Form);

    [Fact]
    public void FiveFeetFiveInchesIsStoredInMetres()
    {
        var (form, _) = ProfileValidation.Validate(new ProfileInput(null, null, null, 165.1), nameRequired: false);

        Assert.Equal(1.651, form!.HeightMetres);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("m")]
    [InlineData("nonbinary")]
    public void OnlyTheTwoValuesTheMathBranchesOnAreAccepted(string sex) =>
        Assert.Null(ProfileValidation.Validate(new ProfileInput(null, sex, null, null), nameRequired: false).Form);

    [Fact]
    public void LeavingSexBlankIsAllowedAndMeansUnknown()
    {
        var (form, error) = ProfileValidation.Validate(new ProfileInput("X", null, null, null), nameRequired: true);

        Assert.Null(error);
        Assert.Null(form!.BiologicalSex);
    }

    [Theory]
    [InlineData("21/03/1985")]
    [InlineData("1985-3-21")]
    [InlineData("not a date")]
    [InlineData("2999-01-01")]
    [InlineData("1800-01-01")]
    public void ABadOrImpossibleDateOfBirthIsRejected(string dob) =>
        Assert.Null(ProfileValidation.Validate(new ProfileInput(null, null, dob, null), nameRequired: false).Form);

    [Fact]
    public void ANameIsRequiredToCreateSomeoneButNotToEditThem()
    {
        Assert.Null(ProfileValidation.Validate(new ProfileInput(null, null, null, null), nameRequired: true).Form);
        Assert.NotNull(ProfileValidation.Validate(new ProfileInput(null, null, null, null), nameRequired: false).Form);
    }

    [Fact]
    public void AnAbsurdlyLongNameIsRejected() =>
        Assert.Null(ProfileValidation.Validate(new ProfileInput(new string('x', 61), null, null, null), nameRequired: true).Form);

    // ── The whole loop, through the real controller and a real database ─────────

    [Fact]
    public async Task TypeAHeightThenLetTheSyncRunAndItIsStillThereAndSaysWhoOwnsIt()
    {
        var (_, repo) = TestHelper.CreateFreshDb();
        var ctrl = new ProfileController(repo, new ProfileContext());

        // 5 ft 5 in, as the browser converts it.
        Assert.IsType<OkObjectResult>(await ctrl.Put(new ProfileInput("Me", "male", null, 165.1)));

        // The nightly sync: Oura has no height.
        await repo.SaveProfileAsync(ProfileMerge.FromSync(await repo.GetProfileAsync(), FromOura(height: null)));

        var json = JsonSerializer.Serialize(((OkObjectResult)await ctrl.Get()).Value, Opts);

        Assert.Contains("\"heightCm\":165.1", json);
        Assert.Contains("\"height\":\"you\"", json);     // says WHO set it, so a value never changes unexplained
        Assert.Contains("\"name\":\"Me\"", json);
    }

    [Fact]
    public async Task ARejectedProfileLeavesTheStoredOneAlone()
    {
        var (_, repo) = TestHelper.CreateFreshDb();
        var ctrl = new ProfileController(repo, new ProfileContext());
        await ctrl.Put(new ProfileInput("Me", "male", null, 165.1));

        Assert.IsType<BadRequestObjectResult>(await ctrl.Put(new ProfileInput("Me", "male", null, 5.5)));

        Assert.Equal(1.651, (await repo.GetProfileAsync())!.Height);
    }
}
