using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

[ApiController, Route("api/stress")]
public class StressController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14)
    {
        var to = LocalTime.Today;
        return Ok(await repo.GetStressAsync(to.AddDays(-days), to));
    }
}
