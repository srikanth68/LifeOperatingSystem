namespace San.Domain.Entities;

public class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public DateTime DueAt { get; set; }
    public bool Done { get; set; } = false;
    public bool NotifyTelegram { get; set; } = true;
    public DateTime? NotifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
