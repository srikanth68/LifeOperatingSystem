using System.Text.Json;
using Microsoft.Extensions.Logging;
using San.Application.DTOs;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.Infrastructure.Context;

public class ContextReceiver(ISanRepository repo, IHttpClientFactory httpFactory, ILogger<ContextReceiver> logger)
    : IContextReceiver
{
    public async Task<ContextPushResult> ProcessPushAsync(ContextPushRequest request)
    {
        var parts = new List<string>();

        // Location
        if (request.Location is { } loc)
        {
            await repo.AddLocationUpdateAsync(new LocationUpdate
            {
                Latitude = loc.Latitude,
                Longitude = loc.Longitude,
                Address = loc.Address,
                Timestamp = request.Timestamp
            });
            parts.Add("location");
        }

        // Calendar events from iPhone
        if (request.CalendarEvents is { Count: > 0 } events)
        {
            foreach (var ce in events)
            {
                await repo.UpsertCalendarEventAsync(new CalendarEvent
                {
                    Title = ce.Title,
                    StartTime = ce.StartTime,
                    EndTime = ce.EndTime,
                    Location = ce.Location,
                    AllDay = ce.AllDay,
                    Source = "ical",
                    ExternalId = $"ical_{ce.Title}_{ce.StartTime:yyyyMMddHHmm}"
                });
            }
            parts.Add($"{events.Count} calendar events");
        }

        // Health data
        if (request.Health is { } health)
        {
            var json = health.RawJson ?? JsonSerializer.Serialize(health);
            await repo.AddActivitySnapshotAsync(new ActivitySnapshot
            {
                Source = "iphone",
                Category = "health",
                DataJson = json,
                Timestamp = request.Timestamp
            });
            parts.Add("health");

            // Fire-and-forget push to Vitara
            _ = Task.Run(async () =>
            {
                try
                {
                    var vitaraUrl = Environment.GetEnvironmentVariable("VITARA_API_URL") ?? "http://localhost:5100";
                    var client = httpFactory.CreateClient();
                    await client.PostAsync(
                        $"{vitaraUrl}/api/health/push",
                        new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Vitara health push failed (non-critical)");
                }
            });
        }

        var summary = parts.Count > 0 ? $"Received: {string.Join(", ", parts)}" : "No data in push";
        return new ContextPushResult(true, summary);
    }
}
