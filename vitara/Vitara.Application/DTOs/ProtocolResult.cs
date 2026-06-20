namespace Vitara.Application.DTOs;

public record ProtocolResult(
    string Name,
    string Icon,
    string Target,
    string Desc,
    string Status,        // "on-track" | "behind" | "suggested" | "manual"
    double? ProgressPct,  // 0-100, null when not measurable from Oura data
    string? Metric        // human-readable current value, e.g. "92 / 150 min this week"
);
