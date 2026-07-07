using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/vo2max")]
public class Vo2MaxController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 90)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await repo.GetVo2MaxAsync(to.AddDays(-days), to));
    }
}
