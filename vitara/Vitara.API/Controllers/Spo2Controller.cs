using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/spo2")]
public class Spo2Controller(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 14)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await repo.GetSpo2Async(to.AddDays(-days), to));
    }
}
