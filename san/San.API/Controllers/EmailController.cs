using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.API.Controllers;

// Connect/manage the mailboxes San.Worker's EmailTriageWorker polls. OAuth callbacks are
// [AllowAnonymous] the same way CalendarController's are — the provider redirects the
// user's browser here directly, with no way to attach San's own auth header.
[ApiController, Route("api/email")]
public class EmailController(ISanRepository repo, IEnumerable<IEmailProviderClient> providers) : ControllerBase
{
    private IEmailProviderClient Resolve(string provider) =>
        providers.FirstOrDefault(p => p.Provider == provider)
        ?? throw new ArgumentException($"Unknown email provider '{provider}'. Use 'google' or 'microsoft'.");

    // Fixed, env-configured redirect URIs — same reasoning as GoogleCalendarService's
    // GOOGLE_REDIRECT_URI: deriving this from the incoming request's Host header breaks
    // behind nginx (the request's Host/Scheme there isn't reliably the public address),
    // and Google/Microsoft require an exact match to a pre-registered redirect URI anyway.
    private static string RedirectUriFor(string provider) => provider switch
    {
        "google" => Environment.GetEnvironmentVariable("GOOGLE_EMAIL_REDIRECT_URI")
            ?? "http://localhost:5300/api/email/auth/google/callback",
        "microsoft" => Environment.GetEnvironmentVariable("MS_REDIRECT_URI")
            ?? "http://localhost:5300/api/email/auth/microsoft/callback",
        _ => throw new ArgumentException($"Unknown email provider '{provider}'."),
    };

    [HttpGet("accounts")]
    public async Task<IActionResult> ListAccounts()
    {
        var accounts = await repo.GetEmailAccountsAsync();
        return Ok(accounts.Select(ToResult));
    }

    [HttpDelete("accounts/{id}")]
    public async Task<IActionResult> DeleteAccount(Guid id) =>
        await repo.DeleteEmailAccountAsync(id) ? NoContent() : NotFound();

    [HttpGet("auth/{provider}")]
    public IActionResult Auth(string provider)
    {
        try
        {
            var client = Resolve(provider);
            return Ok(new { url = client.BuildAuthUrl(RedirectUriFor(provider)) });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("auth/{provider}/callback")]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return BadRequest(new { error = $"{provider} denied the connection: {error}" });
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { error = "Missing code parameter." });

        try
        {
            var client = Resolve(provider);
            var (email, tokenJson) = await client.ExchangeCodeAsync(code, RedirectUriFor(provider));
            var account = await repo.UpsertEmailAccountAsync(provider, email, tokenJson);
            return Ok(new { message = $"{email} connected.", account = ToResult(account) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Failed to connect {provider} account: {ex.Message}" });
        }
    }

    private static object ToResult(EmailAccount a) => new
    {
        a.Id,
        a.Provider,
        a.EmailAddress,
        a.Active,
        a.LastCheckedAt,
        a.CreatedAt,
    };
}
