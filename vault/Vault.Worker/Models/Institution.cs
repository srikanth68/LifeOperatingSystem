namespace Vault.Worker.Models;

public class Institution
{
    public string Id { get; set; } = string.Empty;
    public string PlaidInstitutionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Logo { get; set; }
    public string? Website { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
