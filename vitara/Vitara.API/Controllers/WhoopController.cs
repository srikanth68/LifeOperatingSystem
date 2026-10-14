using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;
using Vitara.Infrastructure.Profiles;
using Vitara.Infrastructure.Whoop;

namespace Vitara.API.Controllers;

// Linking a WHOOP strap (Vitara ADR 0029). The same flow as Oura's: a full-page navigation to
// WHOOP, a callback that files the token under the right person, then a sync.
[ApiController, Route("api/whoop")]
public class WhoopController(IWhoopClient client, IVitaraRepository repo, ProfileContext profile) : ControllerBase
{
    // WHOOP requires `state`, at least eight characters. It carries the person (see
    // ProfileResolver.FromWhoopState), so the second person's strap is never filed under the first.
    [AllowAnonymous]
    [HttpGet("auth")]
    public IActionResult Auth() => Redirect(client.AuthorizeUrl(ProfileResolver.ToWhoopState(profile.ProfileId)));

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error,
                                              [FromQuery(Name = "error_description")] string? description)
    {
        if (!string.IsNullOrEmpty(error)) return Page("WHOOP link failed", $"WHOOP said: {description ?? error}", ok: false);
        if (string.IsNullOrEmpty(code)) return Page("WHOOP link failed", "WHOOP didn't return an authorization code.", ok: false);

        try
        {
            var t = await client.ExchangeCodeAsync(code);
            var token = new WhoopToken
            {
                AccessToken = t.AccessToken,
                RefreshToken = t.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(t.ExpiresIn),
                LinkedAt = DateTime.UtcNow,
            };
            await repo.SaveWhoopTokenAsync(token);

            // The first thirty days straight away, so the app has something to show when the
            // person comes back to it, instead of waiting for tonight's run.
            var first = await WhoopSync.RunAsync(repo, client, token, 30);
            return Page("WHOOP linked ✓",
                first.Ok ? $"Read {first.Sleep} nights and {first.Workouts} workouts. You can close this tab and go back to Vitara."
                         : $"Linked, but the first sync failed ({first.Error}). Vitara will try again tonight.", ok: true);
        }
        catch (Exception ex)
        {
            return Page("WHOOP link failed",
                $"{ex.Message} The usual cause is the redirect URI: the server sends {Request.Scheme}://{Request.Host}/api/whoop/callback " +
                "and that exact address must be listed for the app in the WHOOP developer dashboard.", ok: false);
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var t = await repo.GetWhoopTokenAsync();
        if (t is null) return Ok(new { linked = false, healthy = false, reason = "WHOOP is not linked." });

        // Expiry is not a fault here: WHOOP's tokens last an hour and every sync refreshes.
        var healthy = !t.IsStale && t.LastSyncError is null;
        return Ok(new
        {
            linked = true,
            healthy,
            stale = t.IsStale,
            linkedAt = t.LinkedAt,
            lastSyncedAt = t.LastSyncedAt,
            lastSyncAttemptAt = t.LastSyncAttemptAt,
            lastSyncError = t.LastSyncError,
            reason = healthy ? null
                : t.LastSyncError is not null ? $"Last sync failed: {t.LastSyncError}"
                : "No successful sync in over two days.",
        });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromQuery] int days = 7)
    {
        var t = await repo.GetWhoopTokenAsync();
        if (t is null) return BadRequest(new { error = "WHOOP is not linked." });
        var r = await WhoopSync.RunAsync(repo, client, t, Math.Clamp(days, 1, 90));
        return r.Ok ? Ok(r) : StatusCode(502, new { error = r.Error });
    }

    // Forgets the link here. Access is also revocable from the WHOOP app.
    [HttpDelete("unlink")]
    public async Task<IActionResult> Unlink()
    {
        if (await repo.GetWhoopTokenAsync() is null) return NotFound();
        await repo.DeleteWhoopTokenAsync();
        return NoContent();
    }

    private ContentResult Page(string title, string detail, bool ok) => Content(
        "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'></head>" +
        "<body style='font-family:-apple-system,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1.2rem;line-height:1.5;text-align:center'>" +
        $"<h2>{System.Net.WebUtility.HtmlEncode(title)}</h2>" +
        $"<p style='color:{(ok ? "#333" : "#b00020")}'>{System.Net.WebUtility.HtmlEncode(detail)}</p></body></html>",
        "text/html");
}
