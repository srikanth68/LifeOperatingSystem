namespace Vitara.Domain.Entities;

public class OuraToken
{
    public int Id { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime LinkedAt { get; set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
}
