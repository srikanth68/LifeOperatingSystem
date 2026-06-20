namespace Vault.Worker.Models;

public class PlaidItem
{
    public string Id { get; set; } = string.Empty;
    public string PlaidItemId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string PlaidInstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
