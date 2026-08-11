using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using San.Application.Interfaces;

namespace San.API.Controllers;

// The probing itself lives in IHealthProbe, not here, because San.Worker needs the
// same answers and is a separate container — it must not have to ask this API over
// HTTP how healthy things are, since that would fail to report the one failure most
// worth reporting.
[ApiController, Route("api/health")]
public class HealthController(IHealthProbe probe) : ControllerBase
{
    // Liveness only. Deliberately anonymous and free of dependencies so it answers
    // even when everything below it is broken — a container orchestrator asking "is
    // this process up" must not get a 500 because NorthStar is down.
    [HttpGet, AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok", module = "san", utc = DateTime.UtcNow });

    // Authenticated: this enumerates internal hostnames, model names and mailbox
    // addresses.
    [HttpGet("deep")]
    public async Task<IActionResult> Deep(CancellationToken ct) => Ok(await probe.RunAsync(ct));
}
