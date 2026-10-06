using Vitara.Domain.Entities;

namespace Vitara.Domain.Health;

// What a person typed, as one piece of state. Null means "no value", and for a field the
// sync can fill it also means "unlock it, the person has nothing to say here".
public record ProfileForm(
    string? Name,
    string? BiologicalSex,
    DateOnly? DateOfBirth,
    double? HeightMetres);

// Who owns each field of a profile.
//
// Two writers touch the same row: the person, through the form, and the Oura sync, every
// night. The old rule was "last write wins, whole row", which is wrong in a specific and
// invisible way. Oura returns nothing for a height nobody entered in the Oura app, so the
// sync wrote null over a height the person had set by hand, and the next morning the BMI
// tile asked for a height the app had been told the day before.
//
// The idea worth keeping is that OWNERSHIP IS PER FIELD AND SAYS WHO. A field entered by
// hand is locked: it is the person's own word and no automatic source overrides it. A field
// nobody touched is the sync's to fill, and the sync only ever fills -- an empty value from
// Oura means "Oura does not know", never "this should now be empty".
public static class ProfileMerge
{
    public const string Age = "age";
    public const string Weight = "weight";
    public const string Height = "height";
    public const string Sex = "sex";
    public const string Email = "email";

    public static IReadOnlySet<string> Locked(UserProfile? profile) =>
        (profile?.LockedFields ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // The nightly sync. Never overwrites a locked field and never writes an empty value.
    public static UserProfile FromSync(UserProfile? existing, UserProfile incoming)
    {
        var result = Copy(existing) ?? new UserProfile { Id = incoming.Id };
        var locked = Locked(existing);

        if (!locked.Contains(Age) && incoming.Age is { } age) result.Age = age;
        if (!locked.Contains(Weight) && incoming.Weight is { } weight) result.Weight = weight;
        if (!locked.Contains(Height) && incoming.Height is { } height) result.Height = height;
        if (!locked.Contains(Sex) && !string.IsNullOrWhiteSpace(incoming.BiologicalSex)) result.BiologicalSex = incoming.BiologicalSex;
        if (!locked.Contains(Email) && !string.IsNullOrWhiteSpace(incoming.Email)) result.Email = incoming.Email;

        result.UpdatedAt = DateTime.UtcNow;
        return result;
    }

    // The form. It is the whole state of the four fields it shows, so a field left empty
    // is cleared and handed back to the sync, while a field filled in is locked.
    public static UserProfile FromForm(UserProfile? existing, ProfileForm form)
    {
        var result = Copy(existing) ?? new UserProfile();
        var locked = Locked(existing).ToHashSet(StringComparer.OrdinalIgnoreCase);

        result.Name = string.IsNullOrWhiteSpace(form.Name) ? null : form.Name.Trim();
        result.DateOfBirth = form.DateOfBirth;    // never synced, so there is nothing to lock

        Own(Height, form.HeightMetres is not null, locked);
        result.Height = form.HeightMetres;

        var sex = string.IsNullOrWhiteSpace(form.BiologicalSex) ? null : form.BiologicalSex.Trim().ToLowerInvariant();
        Own(Sex, sex is not null, locked);
        result.BiologicalSex = sex;

        // Entering a date of birth makes a synced age redundant; clearing it hands age
        // back to the sync rather than leaving a stale one behind.
        if (form.DateOfBirth is { } born) result.Age = result.AgeOn(LocalTime.Today);

        result.LockedFields = locked.Count == 0 ? null : string.Join(',', locked.Order());
        result.UpdatedAt = DateTime.UtcNow;
        return result;
    }

    private static void Own(string field, bool setByHand, HashSet<string> locked)
    {
        if (setByHand) locked.Add(field);
        else locked.Remove(field);
    }

    private static UserProfile? Copy(UserProfile? p) => p is null ? null : new UserProfile
    {
        Id = p.Id,
        Name = p.Name,
        DateOfBirth = p.DateOfBirth,
        Age = p.Age,
        Weight = p.Weight,
        Height = p.Height,
        BiologicalSex = p.BiologicalSex,
        Email = p.Email,
        LockedFields = p.LockedFields,
        UpdatedAt = p.UpdatedAt,
    };
}
