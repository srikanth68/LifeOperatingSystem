using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vault.API.Models;
using Vault.Worker.Data;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InstitutionsController : ControllerBase
{
    private readonly VaultDbContext _db;
    private readonly ILogger<InstitutionsController> _logger;

    public InstitutionsController(VaultDbContext db, ILogger<InstitutionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<InstitutionDto>>> GetAll()
    {
        _logger.LogInformation("Fetching all institutions");

        var institutions = await _db.Institutions
            .Select(i => new InstitutionDto
            {
                Id = i.Id,
                Name = i.Name,
                Logo = i.Logo,
                Website = i.Website
            })
            .ToListAsync();

        return Ok(institutions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<InstitutionDto>> GetById(string id)
    {
        var institution = await _db.Institutions.FindAsync(id);
        if (institution == null)
        {
            return NotFound();
        }

        return Ok(new InstitutionDto
        {
            Id = institution.Id,
            Name = institution.Name,
            Logo = institution.Logo,
            Website = institution.Website
        });
    }
}
