using Maaya.Time;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Vitara.API.Controllers;

namespace Vitara.Tests;

// The browser asks the server which zone days are filed in, so the two cannot disagree.
public class ClockControllerTests
{
    private static string Zone()
    {
        var ok = Assert.IsType<OkObjectResult>(new ClockController().Get());
        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return JsonDocument.Parse(json).RootElement.GetProperty("timezone").GetString()!;
    }

    [Fact]
    public void It_reports_the_zone_the_server_actually_files_days_in() =>
        Assert.Equal(MaayaClock.ZoneId, Zone());

    [Fact]
    public void The_zone_is_never_empty_because_the_page_would_silently_fall_back_to_its_default() =>
        Assert.False(string.IsNullOrWhiteSpace(Zone()));
}
