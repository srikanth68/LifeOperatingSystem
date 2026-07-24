namespace San.Domain.Entities;

// Simple key-value store for San-level settings (e.g. the editable chat system prompt).
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
