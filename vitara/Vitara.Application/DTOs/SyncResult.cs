namespace Vitara.Application.DTOs;

public record SyncResult(
    int SleepSynced,
    int ReadinessSynced,
    int ActivitySynced,
    DateTime SyncedAt,
    string? Error = null
);
