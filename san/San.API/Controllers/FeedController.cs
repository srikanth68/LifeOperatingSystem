using Microsoft.AspNetCore.Mvc;
using San.Application.Interfaces;

namespace San.API.Controllers;

[ApiController, Route("api/feed")]
public class FeedController(IModuleContextService moduleContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await moduleContext.GetActivityFeedAsync());
}
