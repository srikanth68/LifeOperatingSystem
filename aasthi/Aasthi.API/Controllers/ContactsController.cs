using Microsoft.AspNetCore.Mvc;
using Aasthi.Application.DTOs;
using Aasthi.Application.Interfaces;
using Aasthi.Domain.Entities;

namespace Aasthi.API.Controllers;

[ApiController, Route("api/properties/{propertyId:guid}/contacts")]
public class ContactsController(IAasthiRepository repo) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Guid propertyId, ContactUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");

        var contact = new PropertyContact
        {
            Name = req.Name,
            Role = req.Role,
            Phone = req.Phone ?? "",
            Email = req.Email ?? "",
            Notes = req.Notes ?? "",
        };

        var created = await repo.AddContactAsync(propertyId, contact);
        if (created is null) return NotFound("Property not found.");
        return Ok(new ContactResult(created.Id, created.PropertyId, created.Name, created.Role, created.Phone, created.Email, created.Notes));
    }

    [HttpPut("{contactId:guid}")]
    public async Task<IActionResult> Update(Guid propertyId, Guid contactId, ContactUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");

        var contact = new PropertyContact
        {
            Id = contactId,
            Name = req.Name,
            Role = req.Role,
            Phone = req.Phone ?? "",
            Email = req.Email ?? "",
            Notes = req.Notes ?? "",
        };

        var ok = await repo.UpdateContactAsync(propertyId, contact);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{contactId:guid}")]
    public async Task<IActionResult> Delete(Guid propertyId, Guid contactId)
    {
        var ok = await repo.DeleteContactAsync(propertyId, contactId);
        return ok ? NoContent() : NotFound();
    }
}
