using Microsoft.AspNetCore.Mvc;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

[ApiController, Route("api/people")]
public class PeopleController(ISanRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q) =>
        Ok(await repo.GetPeopleAsync(q));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var p = await repo.GetPersonAsync(id);
        return p is null ? NotFound() : Ok(p);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PersonRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");
        var person = new Person
        {
            Name = req.Name.Trim(),
            Phone = req.Phone?.Trim(),
            Email = req.Email?.Trim(),
            Birthday = req.Birthday?.Trim(),
            Relationship = req.Relationship ?? "other",
            Notes = req.Notes?.Trim(),
            Tags = req.Tags?.Trim(),
        };
        return Ok(await repo.AddPersonAsync(person));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] PersonRequest req)
    {
        var updated = await repo.UpdatePersonAsync(id, p =>
        {
            if (req.Name is not null) p.Name = req.Name.Trim();
            if (req.Phone is not null) p.Phone = req.Phone.Trim();
            if (req.Email is not null) p.Email = req.Email.Trim();
            if (req.Birthday is not null) p.Birthday = req.Birthday.Trim();
            if (req.Relationship is not null) p.Relationship = req.Relationship;
            if (req.Notes is not null) p.Notes = req.Notes.Trim();
            if (req.Tags is not null) p.Tags = req.Tags.Trim();
        });
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await repo.DeletePersonAsync(id) ? NoContent() : NotFound();

    [HttpGet("birthdays")]
    public async Task<IActionResult> Birthdays([FromQuery] int days = 30) =>
        Ok(await repo.GetUpcomingBirthdaysAsync(days));
}

public record PersonRequest(
    string? Name, string? Phone, string? Email, string? Birthday,
    string? Relationship, string? Notes, string? Tags
);
