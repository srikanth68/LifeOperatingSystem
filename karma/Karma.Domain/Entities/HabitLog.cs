namespace Karma.Domain.Entities;

public class HabitLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HabitId { get; set; }
    public DateOnly Date { get; set; }
    public bool Completed { get; set; }
    public string? Note { get; set; }
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
}
