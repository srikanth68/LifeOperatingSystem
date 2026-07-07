using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/workouts")]
public class WorkoutsController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await repo.GetWorkoutsAsync(to.AddDays(-days), to));
    }
}
