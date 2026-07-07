using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using San.Application.DTOs;
using San.Application.Interfaces;

namespace San.API.Controllers;

[ApiController, Route("api/context")]
public class ContextController(ISanRepository repo, IContextReceiver contextReceiver) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("push")]
    public async Task<IActionResult> Push([FromBody] ContextPushRequest req)
    {
        var expectedKey = Environment.GetEnvironmentVariable("DEVICE_API_KEY") ?? "changeme";
        var deviceKey = Request.Headers["X-Device-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(deviceKey) || deviceKey != expectedKey)
            return Unauthorized(new { error = "Invalid or missing X-Device-Key header." });

        var result = await contextReceiver.ProcessPushAsync(req);
        return Ok(result);
    }

    [HttpGet("latest")]
    public async Task<IActionResult> Latest()
    {
        var location = await repo.GetLatestLocationAsync();
        var snapshots = await repo.GetRecentActivitySnapshotsAsync(10);

        return Ok(new
        {
            location = location is not null ? new
            {
                location.Latitude,
                location.Longitude,
                location.Address,
                location.Timestamp
            } : null,
            recentActivity = snapshots.Select(s => new
            {
                s.Source,
                s.Category,
                s.DataJson,
                s.Timestamp
            })
        });
    }
}
