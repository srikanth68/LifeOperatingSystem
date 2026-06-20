namespace Vault.Worker.Models;

public class CategoryGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public string Color { get; set; } = "#c9a227";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<CategoryGroupItem> Items { get; set; } = new List<CategoryGroupItem>();
}

public class CategoryGroupItem
{
    public string Id { get; set; } = string.Empty;
    public string CategoryGroupId { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty; // matches merchant name / description (contains, case-insensitive)
    public bool IsIncome { get; set; } = false;
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }

    public CategoryGroup? Group { get; set; }
}
