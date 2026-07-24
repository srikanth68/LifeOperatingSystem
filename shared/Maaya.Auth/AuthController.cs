using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Maaya.Auth;

[ApiController, Route("api/auth")]
public class AuthController(TokenService tokenService, RefreshTokenStore refreshStore, JwtConfig config, ILogger<AuthController> logger) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var expectedUser = Environment.GetEnvironmentVariable("AUTH_USERNAME") ?? "admin";
        var expectedHash = Environment.GetEnvironmentVariable("AUTH_PASSWORD_HASH") ?? "";

        if (string.IsNullOrEmpty(expectedHash))
            return StatusCode(503, new { error = "Auth not configured. Set AUTH_PASSWORD_HASH in .env" });

        // A valid bcrypt hash is exactly 60 chars starting with "$2". If the container
        // received something else, the env var got mangled in transit (e.g. compose
        // $-interpolation) — logged so we can tell that apart from a wrong password.
        var hashLooksValid = expectedHash.Length == 60 && expectedHash.StartsWith("$2");
        if (!hashLooksValid)
            logger.LogWarning("AUTH_PASSWORD_HASH looks malformed: length={Length}, prefix={Prefix} (expected 60 chars starting with $2). The env var may have been corrupted.",
                expectedHash.Length, expectedHash.Length >= 4 ? expectedHash[..4] : expectedHash);

        if (!string.Equals(request.Username, expectedUser, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Login failed: username mismatch (got '{Got}', expected '{Expected}').", request.Username, expectedUser);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        bool ok;
        try { ok = BCrypt.Net.BCrypt.Verify(request.Password, expectedHash); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Login failed: bcrypt could not parse the stored hash — it's almost certainly corrupted in the environment.");
            return Unauthorized(new { error = "Invalid credentials." });
        }

        if (!ok)
        {
            logger.LogWarning("Login failed: password mismatch for user '{User}' (username was correct; hash format valid={Valid}).", expectedUser, hashLooksValid);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        logger.LogInformation("Login succeeded for user '{User}'.", expectedUser);

        var userId = "maaya-owner";
        var accessToken = tokenService.GenerateAccessToken(userId, expectedUser);
        var refreshToken = tokenService.GenerateRefreshToken();

        refreshStore.Store(new RefreshTokenEntry
        {
            Token = refreshToken,
            UserId = userId,
            Username = expectedUser,
            ExpiresAt = DateTime.UtcNow.AddDays(config.RefreshTokenDays),
        });

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = config.AccessTokenMinutes * 60,
            Username = expectedUser,
        });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshRequest request)
    {
        var entry = refreshStore.Get(request.RefreshToken);

        if (entry is null || entry.Revoked || entry.ExpiresAt < DateTime.UtcNow)
            return Unauthorized(new { error = "Invalid or expired refresh token." });

        refreshStore.Revoke(request.RefreshToken);

        var accessToken = tokenService.GenerateAccessToken(entry.UserId, entry.Username);
        var newRefreshToken = tokenService.GenerateRefreshToken();

        refreshStore.Store(new RefreshTokenEntry
        {
            Token = newRefreshToken,
            UserId = entry.UserId,
            Username = entry.Username,
            ExpiresAt = DateTime.UtcNow.AddDays(config.RefreshTokenDays),
        });

        refreshStore.Cleanup();

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = config.AccessTokenMinutes * 60,
            Username = entry.Username,
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout([FromBody] RefreshRequest? request)
    {
        if (request?.RefreshToken is not null)
            refreshStore.Revoke(request.RefreshToken);
        else
            refreshStore.RevokeAll();

        return Ok(new { message = "Logged out." });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var username = User.Identity?.Name;
        var sub = User.FindFirst("sub")?.Value;
        return Ok(new { userId = sub, username });
    }

    [AllowAnonymous]
    [HttpGet("probe")]
    public IActionResult Probe()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        var trusted = remoteIp is not null && TrustedNetwork.IsTrusted(remoteIp);
        var pin = Environment.GetEnvironmentVariable("AUTH_PIN") ?? "";
        return Ok(new
        {
            trusted,
            method = trusted && pin.Length > 0 ? "pin" : "credentials",
            pinLength = trusted && pin.Length > 0 ? pin.Length : 0,
        });
    }

    [AllowAnonymous]
    [HttpPost("pin")]
    public IActionResult PinLogin([FromBody] PinRequest request)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null || !TrustedNetwork.IsTrusted(remoteIp))
            return Unauthorized(new { error = "PIN login requires a trusted network." });

        var expectedPin = Environment.GetEnvironmentVariable("AUTH_PIN") ?? "";
        if (expectedPin.Length == 0)
            return StatusCode(503, new { error = "AUTH_PIN not configured." });

        if (!string.Equals(request.Pin, expectedPin, StringComparison.Ordinal))
            return Unauthorized(new { error = "Wrong PIN." });

        return IssueTokens();
    }

    [AllowAnonymous]
    [HttpPost("auto")]
    public IActionResult AutoLogin()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is null || !TrustedNetwork.IsTrusted(remoteIp))
            return Unauthorized(new { error = "Untrusted network.", trusted = false });

        return IssueTokens();
    }

    private IActionResult IssueTokens()
    {
        var username = Environment.GetEnvironmentVariable("AUTH_USERNAME") ?? "admin";
        var userId = "maaya-owner";
        var accessToken = tokenService.GenerateAccessToken(userId, username);
        var refreshToken = tokenService.GenerateRefreshToken();

        refreshStore.Store(new RefreshTokenEntry
        {
            Token = refreshToken,
            UserId = userId,
            Username = username,
            ExpiresAt = DateTime.UtcNow.AddDays(config.RefreshTokenDays),
        });

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = config.AccessTokenMinutes * 60,
            Username = username,
        });
    }

    [AllowAnonymous]
    [HttpPost("hash")]
    public IActionResult HashPassword([FromBody] HashRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Password is required." });

        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);
        return Ok(new { hash });
    }
}

public record LoginRequest(string Username, string Password);
public record PinRequest(string Pin);
public record RefreshRequest(string RefreshToken);
public record HashRequest(string Password);

public class TokenResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public int ExpiresIn { get; set; }
    public string Username { get; set; } = "";
}
