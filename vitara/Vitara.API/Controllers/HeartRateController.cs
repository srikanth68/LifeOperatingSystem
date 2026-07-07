using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;

namespace Vitara.API.Controllers;

[ApiController, Route("api/heartrate")]
public class HeartRateController(IVitaraRepository repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int hours = 24)
    {
        var to = DateTime.UtcNow;
        var from = to.AddHours(-hours);
        var data = await repo.GetHeartRateAsync(from, to);
        return Ok(data.Select(h => new { h.Timestamp, h.Bpm, h.Source }));
    }
}
