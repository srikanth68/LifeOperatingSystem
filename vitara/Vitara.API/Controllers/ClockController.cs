using Maaya.Time;
using Microsoft.AspNetCore.Mvc;

namespace Vitara.API.Controllers;

// Which timezone this server files days in.
//
// The browser needs to agree with it. The server decides which day a night of sleep belongs to;
// a page that guessed its own zone would sometimes call a different day "today", and a reading
// would appear to be filed under tomorrow. So the page asks, rather than being configured
// separately and drifting.
//
// Read-only and tiny on purpose. It reports the zone; it does not change it (that is
// MAAYA_TIMEZONE, set where the service is deployed).
[ApiController, Route("api/clock")]
public class ClockController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { timezone = MaayaClock.ZoneId });
}
