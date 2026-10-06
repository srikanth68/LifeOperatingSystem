using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

[ApiController, Route("api/resilience")]
public class ResilienceController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14)
    {
        var to = LocalTime.Today;
        return Ok(await repo.GetResilienceAsync(to.AddDays(-days), to));
    }
}
