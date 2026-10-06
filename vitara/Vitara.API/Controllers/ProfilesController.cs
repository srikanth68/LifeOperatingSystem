using Microsoft.AspNetCore.Mvc;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Profiles;

namespace Vitara.API.Controllers;

// What the form sends. Height is centimetres on the wire because that is what a person can
// read off a tape or convert from feet and inches in the browser; the database keeps metres,
// which is what every calculation here already expects.
public record ProfileInput(string? Name, string? BiologicalSex, string? DateOfBirth, double? HeightCm);

// One place that decides what a profile may contain, used by creating and by editing, so
// the two cannot drift into accepting different things.
public static class ProfileValidation
{
    public const int MaxNameLength = 60;

    public static (ProfileForm? Form, string? Error) Validate(ProfileInput input, bool nameRequired)
    {
        var name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim();

        if (nameRequired && name is null)
            return (null, "Give this person a name so they can be told apart.");
        if (name is { Length: > MaxNameLength })
            return (null, $"Keep the name under {MaxNameLength} characters.");

        // Only the two values the reference ranges and the kidney equation actually branch
        // on. Anything else is stored as unknown rather than guessed at.
        var sex = string.IsNullOrWhiteSpace(input.BiologicalSex) ? null : input.BiologicalSex.Trim().ToLowerInvariant();
        if (sex is not null and not ("male" or "female"))
            return (null, "Biological sex is used for reference ranges and kidney function, and only male or female are meaningful there. Leave it blank if you would rather not say.");

        DateOnly? born = null;
        if (!string.IsNullOrWhiteSpace(input.DateOfBirth))
        {
            if (!DateOnly.TryParseExact(input.DateOfBirth.Trim(), "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
                return (null, "Date of birth should look like 1985-03-21.");

            if (parsed > LocalTime.Today || parsed.Year < 1900)
                return (null, "That date of birth is not possible.");

            born = parsed;
        }

        // 100-250 cm catches the two ways this goes wrong: feet typed as if they were
        // centimetres (5.5 -> 5 cm) and a stray digit (1650). Neither should be stored.
        double? metres = null;
        if (input.HeightCm is { } cm)
        {
            if (cm is < 100 or > 250)
                return (null, "Height should be between 100 and 250 cm. If you entered feet and inches, the form converts them for you.");

            metres = Math.Round(cm / 100.0, 3);
        }

        return (new ProfileForm(name, sex, born, metres), null);
    }
}

// The people. Creating and removing them, and listing who exists.
//
// Deliberately NOT scoped to a profile: asking "who is there" is the question you ask in
// order to pick one.
[ApiController, Route("api/profiles")]
public class ProfilesController(ProfileCatalog catalog) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(catalog.List().Select(p => new { p.Id, p.Name, p.IsDefault }));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProfileInput input)
    {
        var (form, error) = ProfileValidation.Validate(input, nameRequired: true);
        if (form is null) return BadRequest(new { error });

        var created = await catalog.CreateAsync(form);
        return Created($"api/profiles/{created.Id}", new { created.Id, created.Name, created.IsDefault });
    }

    // Removes the person and everything about them, which is deleting one file. Irreversible
    // and the only destructive thing in this API, so it asks to be told twice: the id must be
    // repeated in the query, which stops a stray DELETE from a tool or a typo from doing it.
    [HttpDelete("{id}")]
    public IActionResult Delete(string id, [FromQuery] string? confirm)
    {
        if (id == ProfileIds.Default)
            return BadRequest(new { error = "The original profile cannot be deleted." });

        if (!ProfileIds.IsValid(id) || !catalog.Exists(id))
            return NotFound(new { error = "There is no such profile." });

        if (confirm != id)
            return BadRequest(new { error = "This permanently deletes everything recorded for this person. Repeat the id as ?confirm= to go ahead." });

        return catalog.Delete(id) ? NoContent() : NotFound();
    }
}
