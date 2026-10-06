using Vitara.Domain.Health;

namespace Vitara.Domain.Entities;

// One person.
//
// "One person" is now literal: every profile has its own database file, and this row is
// the only one in it. The Id is therefore a row key INSIDE that file, not the person's
// identity -- which person this is, is decided by WHICH file you opened. It stays
// "default" in every database, so nothing that already reads it has to change.
public class UserProfile
{
    public string Id { get; set; } = "default";

    // What this person is called on screen. Never used in a file name or an id: names are
    // personal data, and ids end up in paths, logs and URLs.
    public string? Name { get; set; }

    // Preferred over Age wherever it exists. A stored age is right on the day it was
    // written and wrong for the rest of the year, and eGFR and several reference ranges
    // are read against it.
    public DateOnly? DateOfBirth { get; set; }

    public int? Age { get; set; }
    public double? Weight { get; set; }      // kg
    public double? Height { get; set; }      // m
    public string? BiologicalSex { get; set; }
    public string? Email { get; set; }

    // Which fields a person set by hand, as a comma-separated list. The Oura sync fills
    // everything it can and used to overwrite the whole row each night, which erased a
    // height entered by hand the first time Oura had none to offer. A field named here is
    // the person's own word and the sync leaves it alone. See ProfileMerge.
    public string? LockedFields { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Derived, so not stored: EF maps only properties with a setter.
    public int? CurrentAge => AgeOn(LocalTime.Today);

    public int? AgeOn(DateOnly today)
    {
        if (DateOfBirth is not { } born) return Age;

        var years = today.Year - born.Year;
        if (today < born.AddYears(years)) years--;     // birthday not reached yet this year

        return years is >= 0 and <= 130 ? years : Age;
    }
}
