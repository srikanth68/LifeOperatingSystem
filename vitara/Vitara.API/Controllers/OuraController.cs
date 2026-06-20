using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitara.Application.Interfaces;
using Vitara.Domain.Entities;

namespace Vitara.API.Controllers;

[ApiController, Route("api/oura")]
public class OuraController(IOuraClient client, IVitaraRepository repo, IConfiguration cfg) : ControllerBase
{
    [HttpGet("auth")]
    public IActionResult Auth()
    {
        var clientId    = cfg["Oura:ClientId"];
        var redirectUri = Uri.EscapeDataString(cfg["Oura:RedirectUri"] ?? "http://localhost:5100/api/oura/callback");
        var url = $"https://cloud.ouraring.com/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={redirectUri}&scope=daily+personal+session+tag+workout";
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code)
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

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var token = await repo.GetTokenAsync();
        if (token is null) return Ok(new { linked = false });
        return Ok(new { linked = true, expired = token.IsExpired, linkedAt = token.LinkedAt });
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

        return Ok(new { sleep = sleep.Count, readiness = readiness.Count, activity = activity.Count, syncedAt = DateTime.UtcNow });
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
