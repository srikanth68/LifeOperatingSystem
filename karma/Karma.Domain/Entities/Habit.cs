using System.Text.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karma.Domain.Entities;

public class Habit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Emoji { get; set; } = "✅";
    public string Category { get; set; } = "personal";
    public string? NotifyTime { get; set; }   // "HH:mm", null = off
    public string? NotifyMessage { get; set; }
    public string NotifyChannel { get; set; } = "telegram";
    public string NotifyDaysJson { get; set; } = "[0,1,2,3,4,5,6]";
    public bool IsActive { get; set; } = true;
    public DateOnly? LastNotificationSentOn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public int[] NotifyDays
    {
        get => JsonSerializer.Deserialize<int[]>(NotifyDaysJson) ?? [0, 1, 2, 3, 4, 5, 6];
        set => NotifyDaysJson = JsonSerializer.Serialize(value);
    }
}
