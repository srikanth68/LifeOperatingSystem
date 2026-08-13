using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Maaya.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddMaayaAuth(this IServiceCollection services, bool isAuthServer = false)
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException("JWT_SECRET environment variable is required.");

        var config = new JwtConfig
        {
            Secret = secret,
            AccessTokenMinutes = int.TryParse(Environment.GetEnvironmentVariable("JWT_ACCESS_MINUTES"), out var m) ? m : 60,
            RefreshTokenDays = int.TryParse(Environment.GetEnvironmentVariable("JWT_REFRESH_DAYS"), out var d) ? d : 30,
        };

        services.AddSingleton(config);
        services.AddSingleton<TokenService>();

        if (isAuthServer)
            services.AddSingleton<RefreshTokenStore>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config.Issuer,
                    ValidAudience = config.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Secret)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization(opts =>
        {
            opts.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        return services;
    }

    public static WebApplication UseMaayaAuth(this WebApplication app, bool isAuthServer = false)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        if (isAuthServer) LogAuthConfig(app);
        return app;
    }

    // Says, at boot, whether logging in can possibly work — because the failure modes
    // are all silent. A missing AUTH_PIN doesn't error, it just quietly turns the PIN
    // pad into the credentials form; a doubled-dollar hash doesn't error, it just
    // rejects the right password forever. Both are one line in `docker compose logs
    // vault` here, and otherwise need a shell inside a running container to find.
    //
    // Deliberately logs shapes, never values: lengths and validity, no hash, no PIN.
    private static void LogAuthConfig(WebApplication app)
    {
        var raw = Environment.GetEnvironmentVariable("AUTH_PASSWORD_HASH") ?? "";
        var pin = Environment.GetEnvironmentVariable("AUTH_PIN") ?? "";
        var user = Environment.GetEnvironmentVariable("AUTH_USERNAME") ?? "admin";
        var log = app.Logger;

        if (raw.Length == 0)
            log.LogError("AUTH: no AUTH_PASSWORD_HASH set — password login will return 503.");
        else if (AuthSecrets.NeededUndoubling(raw))
            log.LogWarning("AUTH: AUTH_PASSWORD_HASH arrived with doubled '$' ({Length} chars) and was repaired at runtime. " +
                           "'$$' is the escape for a compose `environment:` block; values under `env_file:` are taken literally. " +
                           "Write it with single '$' to remove the need for this.", raw.Length);
        else if (!AuthSecrets.LooksLikeBcrypt(raw))
            log.LogError("AUTH: AUTH_PASSWORD_HASH is not a bcrypt hash ({Length} chars, expected 60 starting '$2') — every password will be rejected.", raw.Length);
        else
            log.LogInformation("AUTH: password hash OK for user '{User}'.", user);

        if (pin.Length == 0)
            log.LogWarning("AUTH: no AUTH_PIN set — the PIN pad will not appear and login falls back to username + password.");
        else
            log.LogInformation("AUTH: PIN configured ({Length} digits). Trusted networks: {Networks}",
                pin.Length, Environment.GetEnvironmentVariable("AUTH_TRUSTED_NETWORKS") is { Length: > 0 } n ? n : "(private ranges only)");
    }
}
