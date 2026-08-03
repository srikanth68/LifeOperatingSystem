using Microsoft.AspNetCore.Mvc;
using San.Application;
using San.Application.Interfaces;

namespace San.API.Controllers;

// The audit itself runs in San.Worker; this only exposes the instruction that steers
// it, so what San proactively watches for is tunable without a rebuild — same
// arrangement as the chat and email-triage prompts.
[ApiController, Route("api/audit")]
public class AuditController(ISanRepository repo) : ControllerBase
{
    [HttpGet("prompt")]
    public async Task<IActionResult> GetPrompt()
    {
        var stored = await repo.GetSettingAsync(SystemAuditDefaults.PromptKey);
        return Ok(new
        {
            prompt = stored ?? SystemAuditDefaults.Prompt,
            isDefault = stored is null,
            defaultPrompt = SystemAuditDefaults.Prompt,
        });
    }

    [HttpPut("prompt")]
    public async Task<IActionResult> SetPrompt([FromBody] AuditPromptRequest req)
    {
        await repo.SetSettingAsync(SystemAuditDefaults.PromptKey, req.Prompt ?? "");
        return Ok(new { prompt = req.Prompt ?? "" });
    }

    // What the last run reported — also what's fed back in to stop it repeating itself.
    // Clearing it makes the next run treat everything as new, which is the way to
    // re-surface a finding you dismissed.
    [HttpGet("last-findings")]
    public async Task<IActionResult> GetLastFindings()
    {
        var findings = await repo.GetSettingAsync(SystemAuditDefaults.LastFindingsKey);
        return Ok(new { findings });
    }

    [HttpDelete("last-findings")]
    public async Task<IActionResult> ClearLastFindings()
    {
        await repo.SetSettingAsync(SystemAuditDefaults.LastFindingsKey, "");
        return NoContent();
    }
}

public record AuditPromptRequest(string? Prompt);
