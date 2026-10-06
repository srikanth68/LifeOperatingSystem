using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Health;

namespace Vitara.API.Controllers;

[ApiController, Route("api/vo2max")]
public class Vo2MaxController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 90)
    {
        var to = LocalTime.Today;
        return Ok(await repo.GetVo2MaxAsync(to.AddDays(-days), to));
    }
}
