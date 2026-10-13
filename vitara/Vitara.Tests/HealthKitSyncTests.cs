using Microsoft.AspNetCore.Mvc;
using Vitara.API.Controllers;
using Vitara.Domain.Health;

namespace Vitara.Tests;

// Vitara's own iOS app posts the companion's bundle to /api/healthkit/sync, signed in
// instead of carrying a device key. Same filing as the companion's ingest.
public class HealthKitSyncTests
{
    private static HealthKitPayload Bundle() => new(
        Steps: 9100, HeartRate: null, ActiveCalories: 450, SleepHours: null, SleepStart: null, SleepEnd: null,
        Timestamp: DateTime.UtcNow,
        DailyActivity: [new HealthKitDailyActivity("2026-09-01", 7000, 300)]);

    [Fact]
    public async Task SyncFilesStepsUnderThePhonesOwnRow_WithoutADeviceKey()
    {
        var repo = new FakeRepo();
        var ctrl = new HealthKitController(repo);

        Assert.IsType<OkObjectResult>(await ctrl.Sync(Bundle()));

        Assert.All(repo.ActivityData, a => Assert.Equal(SourceResolution.PhoneHealth, a.Source));
        Assert.Contains(repo.ActivityData, a => a.Steps == 9100);
        Assert.Contains(repo.ActivityData, a => a.Day == new DateOnly(2026, 9, 1) && a.Steps == 7000);
    }

    [Fact]
    public async Task TheCompanionsIngestStillNeedsItsKey()
    {
        var ctrl = new HealthKitController(new FakeRepo())
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };
        Assert.IsType<UnauthorizedObjectResult>(await ctrl.Ingest(Bundle()));
    }
}
