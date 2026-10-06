using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Domain.Health;
using Vitara.Infrastructure.Profiles;

namespace Vitara.API.Controllers;

// The person this request is about: whoever the X-Profile-Id header names, or the original
// profile when there is none. Readable by everything that already read it, and now writable.
[ApiController, Route("api/profile")]
public class ProfileController(IVitaraRepository repo, ProfileContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(Shape(await repo.GetProfileAsync()));

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] ProfileInput input)
    {
        var (form, error) = ProfileValidation.Validate(input, nameRequired: false);
        if (form is null) return BadRequest(new { error });

        var existing = await repo.GetProfileAsync();
        var merged = ProfileMerge.FromForm(existing, form);

        await repo.SaveProfileAsync(merged);
        return Ok(Shape(merged));
    }

    private object Shape(UserProfile? p)
    {
        if (p is null) return new { synced = false, id = context.ProfileId };

        var locked = ProfileMerge.Locked(p);

        // Who each value came from, said rather than implied. "you" means it was typed in and
        // no automatic source will touch it; "sync" means the Oura sync filled it and will keep
        // it current. The form shows this beside each field, because the surprise it prevents
        // is a value changing, or not changing, with no explanation.
        string? Source(string field, bool has) => locked.Contains(field) ? "you" : has ? "sync" : null;

        return new
        {
            synced = true,
            id = context.ProfileId,
            p.Name,
            age = p.CurrentAge,
            dateOfBirth = p.DateOfBirth?.ToString("yyyy-MM-dd"),
            p.Weight,
            p.Height,                                              // metres, as it always was
            heightCm = p.Height is { } m ? Math.Round(m * 100, 1) : (double?)null,
            p.BiologicalSex,
            p.Email,
            sources = new
            {
                height = Source(ProfileMerge.Height, p.Height is not null),
                sex = Source(ProfileMerge.Sex, !string.IsNullOrWhiteSpace(p.BiologicalSex)),
            },
        };
    }
}
