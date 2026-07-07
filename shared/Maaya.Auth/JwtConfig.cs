namespace Maaya.Auth;

public sealed class JwtConfig
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "maaya";
    public string Audience { get; set; } = "maaya-frontend";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;
}
