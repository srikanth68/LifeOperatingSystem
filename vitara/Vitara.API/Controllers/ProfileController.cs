using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/profile")]
public class ProfileController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var p = await repo.GetProfileAsync();
        if (p is null) return Ok(new { synced = false });
        return Ok(new { synced = true, p.Age, p.Weight, p.Height, p.BiologicalSex, p.Email });
    }
}
