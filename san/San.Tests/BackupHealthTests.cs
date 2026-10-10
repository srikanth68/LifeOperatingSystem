using San.Application.Interfaces;
using San.Infrastructure.Health;

namespace San.Tests;

// San reads the status file the backup job leaves in the data directory, because the job
// runs on the host and nothing in the stack would otherwise notice it stopping.
//
// The job became weekly (it stops the stack for a minute, so it does not run nightly).
// The check assumed nightly and would have called every week of silence a failure; the
// status file now says how often to expect a run, and the check believes it.
public class BackupHealthTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static string Status(bool ok, DateTime lastRun, int? everyHours = 168, string error = "") =>
        $$"""
        {
          "lastRunUtc": "{{lastRun:yyyy-MM-ddTHH:mm:ssZ}}",
          "ok": {{(ok ? "true" : "false")}},
          {{(everyHours is null ? "" : $"\"expectedEveryHours\": {everyHours},")}}
          "error": "{{error}}"
        }
        """;

    [Fact]
    public void A_weekly_backup_from_six_days_ago_is_fine()
    {
        Assert.Null(HealthProbe.BackupProblem(Status(true, Now.AddDays(-6)), Now));
    }

    [Fact]
    public void A_weekly_backup_missed_by_more_than_a_day_is_raised()
    {
        var p = HealthProbe.BackupProblem(Status(true, Now.AddDays(-8.5)), Now);

        Assert.NotNull(p);
        Assert.Equal(HealthProblemKeys.Backup, p!.Key);
        Assert.Equal("high", p.Severity);
    }

    [Fact]
    public void A_status_file_from_before_the_schedule_was_written_keeps_the_nightly_rule()
    {
        Assert.Null(HealthProbe.BackupProblem(Status(true, Now.AddHours(-40), everyHours: null), Now));
        Assert.NotNull(HealthProbe.BackupProblem(Status(true, Now.AddHours(-50), everyHours: null), Now));
    }

    [Fact]
    public void A_failed_backup_is_critical_and_names_what_failed()
    {
        var p = HealthProbe.BackupProblem(
            Status(false, Now.AddHours(-1), error: "1 problem(s): san/san.db: database disk image is malformed"), Now);

        Assert.NotNull(p);
        Assert.Equal("critical", p!.Severity);
        Assert.Contains("san/san.db", p.Message);
    }

    [Fact]
    public void A_malformed_status_file_says_nothing_rather_than_guessing()
    {
        Assert.Null(HealthProbe.BackupProblem("{ not json", Now));
    }
}
