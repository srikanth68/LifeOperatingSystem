using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/oura")]
public class OuraController(IOuraClient client, IVitaraRepository repo, IConfiguration cfg) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("auth")]
    public IActionResult Auth()
    {
        var clientId    = cfg["Oura:ClientId"];
        var redirectUri = Uri.EscapeDataString(cfg["Oura:RedirectUri"] ?? "http://localhost:5100/api/oura/callback");
        var url = $"https://cloud.ouraring.com/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={redirectUri}&scope=daily+personal+session+tag+workout";
        return Redirect(url);
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? error)
    {
        // Oura can redirect back with ?error=... (e.g. access_denied) and no code.
        if (!string.IsNullOrEmpty(error))
            return OuraErrorPage($"Oura returned an authorization error: {error}");
        if (string.IsNullOrEmpty(code))
            return OuraErrorPage("Oura didn't return an authorization code.");

        try
        {
            var tokenJson = await client.ExchangeCodeAsync(code);
            var payload   = JsonSerializer.Deserialize<TokenPayload>(tokenJson, _json)
                ?? throw new InvalidOperationException("Empty token payload");

            await repo.SaveTokenAsync(new OuraToken
            {
                AccessToken  = payload.AccessToken,
                RefreshToken = payload.RefreshToken,
                ExpiresAt    = DateTime.UtcNow.AddSeconds(payload.ExpiresIn),
                LinkedAt     = DateTime.UtcNow,
            });

            return Content("<html><body style='font-family:sans-serif;text-align:center;padding:4rem'>" +
                "<h2>Oura linked ✓</h2><p>You can close this tab.</p></body></html>", "text/html");
        }
        catch (HttpRequestException ex)
        {
            // Never reached Oura at all — DNS or egress from the container, which has
            // nothing to do with the app's OAuth config. Blaming the redirect URI here
            // (as this page used to do unconditionally) sends you off editing settings
            // at cloud.ouraring.com that were never the problem.
            return OuraErrorPage(ex.Message, OuraFailure.Unreachable);
        }
        catch (Exception ex)
        {
            // Oura answered, and rejected it — now a redirect-URI/credentials mismatch
            // really is the likeliest cause.
            return OuraErrorPage(ex.Message, OuraFailure.Rejected);
        }
    }

    private enum OuraFailure { Unreachable, Rejected }

    private ContentResult OuraErrorPage(string detail, OuraFailure kind = OuraFailure.Rejected)
    {
        var redirect     = cfg["Oura:RedirectUri"] ?? "(unset)";
        var safeDetail   = System.Net.WebUtility.HtmlEncode(detail);
        var safeRedirect = System.Net.WebUtility.HtmlEncode(redirect);

        var guidance = kind == OuraFailure.Unreachable
            ? "<p>This container <b>could not reach api.ouraring.com</b> — the request failed before " +
              "Oura ever saw it, so this is a network/DNS problem, <i>not</i> your app's redirect URI.</p>" +
              "<p>Check outbound DNS from the container:</p>" +
              "<pre style='background:#f4f4f4;padding:.75rem;border-radius:6px'>" +
              "docker compose exec vitara getent hosts api.ouraring.com</pre>" +
              "<p>If that resolves nothing while the host machine can, Docker's DNS is the culprit — " +
              "a VPN or mesh network on the host is the usual reason. Restarting Docker Desktop " +
              "normally clears it.</p>"
            : "<p>The most common cause is a <b>redirect URI mismatch</b>. This app sent Oura:</p>" +
              $"<pre style='background:#f4f4f4;padding:.75rem;border-radius:6px'>{safeRedirect}</pre>" +
              "<p>That <i>exact</i> string (scheme, host, port, path — no trailing slash) must be listed " +
              "under <b>Redirect URIs</b> for your app at cloud.ouraring.com. If it isn't, add it there and try again.</p>";

        return Content(
            "<html><body style='font-family:sans-serif;max-width:42rem;margin:4rem auto;padding:0 1rem;line-height:1.5'>" +
            "<h2>Oura link failed</h2>" +
            $"<p style='color:#b00020'><b>{safeDetail}</b></p>" +
            guidance +
            "</body></html>", "text/html");
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var token = await repo.GetTokenAsync();
        if (token is null) return Ok(new { linked = false });
        return Ok(new { linked = true, expired = token.IsExpired, linkedAt = token.LinkedAt, lastSyncedAt = token.LastSyncedAt });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var token = await repo.GetTokenAsync();
        if (token is null) return BadRequest(new { error = "Not linked" });

        if (token.IsExpired)
        {
            var refreshed = await client.RefreshAccessTokenAsync(token.RefreshToken);
            var rp = JsonSerializer.Deserialize<TokenPayload>(refreshed, _json)!;
            token.AccessToken  = rp.AccessToken;
            token.RefreshToken = rp.RefreshToken;
            token.ExpiresAt    = DateTime.UtcNow.AddSeconds(rp.ExpiresIn);
            await repo.SaveTokenAsync(token);
        }

        var to   = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);

        var sleep     = await client.GetSleepAsync(token.AccessToken, from, to);
        var readiness = await client.GetReadinessAsync(token.AccessToken, from, to);
        var activity  = await client.GetActivityAsync(token.AccessToken, from, to);

        await repo.UpsertSleepAsync(sleep);
        await repo.UpsertReadinessAsync(readiness);
        await repo.UpsertActivityAsync(activity);

        var syncedAt = DateTime.UtcNow;
        token.LastSyncedAt = syncedAt;
        await repo.SaveTokenAsync(token);

        return Ok(new { sleep = sleep.Count, readiness = readiness.Count, activity = activity.Count, syncedAt });
    }

    [HttpDelete("unlink")]
    public async Task<IActionResult> Unlink()
    {
        var token = await repo.GetTokenAsync();
        if (token is null) return NotFound();
        await repo.DeleteTokenAsync();
        return NoContent();
    }

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private record TokenPayload(
        [property: JsonPropertyName("access_token")]  string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")]    int ExpiresIn
    );
}
