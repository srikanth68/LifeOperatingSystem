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

    // Everything San has notified about, with how many times and when — this is what
    // decides whether a finding repeats, so it's also the place to look when something
    // is either nagging or has gone quiet unexpectedly.
    [HttpGet("ledger")]
    public async Task<IActionResult> GetLedger()
    {
        var now = DateTime.UtcNow;
        var entries = await repo.GetLedgerAsync();
        return Ok(entries.Select(e => new
        {
            e.Key, e.Severity, e.Source, e.NotifyCount, e.LastMessage,
            e.FirstSeenAt, e.LastNotifiedAt, e.DueOn,
            nextEligibleAt = e.LastNotifiedAt + NotifyPolicy.Cooldown(e.Severity, e.NotifyCount, e.DueOn, now),
        }));
    }

    // Clearing makes everything eligible again — the way to re-surface something you
    // dismissed. Delete a single key to un-suppress just that one.
    [HttpDelete("ledger")]
    public async Task<IActionResult> ClearLedger()
    {
        await repo.ClearLedgerAsync();
        return NoContent();
    }
}

public record AuditPromptRequest(string? Prompt);
