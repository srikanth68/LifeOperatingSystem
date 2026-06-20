using Microsoft.AspNetCore.Mvc;

namespace Vault.API.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    [HttpGet("plaid-env")]
    public ActionResult<object> GetPlaidEnv()
    {
        var env = Environment.GetEnvironmentVariable("PLAID_ENV") ?? "sandbox";
        return Ok(new { environment = env });
    }
}
