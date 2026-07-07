using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using San.Application.Interfaces;
using San.Domain.Entities;

namespace San.Infrastructure.Google;

public class GoogleCalendarService(IServiceProvider services, ILogger<GoogleCalendarService> logger) : IGoogleCalendarService
{
    private static readonly string[] Scopes = [CalendarService.Scope.CalendarReadonly];
    private static readonly string TokenFile = Path.Combine(Directory.GetCurrentDirectory(), "..", "google_tokens.json");

    private string? ClientId => Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
    private string? ClientSecret => Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
    private string RedirectUri => Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI")
                                  ?? "http://localhost:5300/api/calendar/callback";

    private bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);

    public bool IsConfiguredAndAuthorized => IsConfigured && File.Exists(TokenFile);

    public string GetAuthUrl()
    {
        if (!IsConfigured) return string.Empty;

        var flow = BuildFlow();
        var uri = flow.CreateAuthorizationCodeRequest(RedirectUri);
        return uri.Build().AbsoluteUri;
    }

    public async Task<bool> HandleCallbackAsync(string code)
    {
        if (!IsConfigured) return false;

        try
        {
            var flow = BuildFlow();
            var token = await flow.ExchangeCodeForTokenAsync("user", code, RedirectUri, CancellationToken.None);
            var json = JsonSerializer.Serialize(token);
            await File.WriteAllTextAsync(TokenFile, json);
            logger.LogInformation("Google Calendar OAuth tokens saved.");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to exchange OAuth code for tokens");
            return false;
        }
    }

    public async Task<int> SyncEventsAsync(CancellationToken ct)
    {
        if (!IsConfiguredAndAuthorized) return 0;

        try
        {
            var tokenJson = await File.ReadAllTextAsync(TokenFile, ct);
            var token = JsonSerializer.Deserialize<TokenResponse>(tokenJson);
            if (token is null) return 0;

            var flow = BuildFlow();
            var credential = new UserCredential(flow, "user", token);

            var service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Maaya San"
            });

            var request = service.Events.List("primary");
            request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow;
            request.TimeMaxDateTimeOffset = DateTimeOffset.UtcNow.AddDays(30);
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.MaxResults = 250;

            var events = await request.ExecuteAsync(ct);
            var count = 0;

            using var scope = services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISanRepository>();

            foreach (var e in events.Items ?? [])
            {
                var start = e.Start?.DateTimeDateTimeOffset?.UtcDateTime
                            ?? (DateTime.TryParse(e.Start?.Date, out var d) ? d : (DateTime?)null);
                var end = e.End?.DateTimeDateTimeOffset?.UtcDateTime
                          ?? (DateTime.TryParse(e.End?.Date, out var d2) ? d2 : (DateTime?)null);

                if (start is null || end is null) continue;

                var calEvent = new CalendarEvent
                {
                    Title = e.Summary ?? "(no title)",
                    Description = e.Description,
                    StartTime = start.Value,
                    EndTime = end.Value,
                    Location = e.Location,
                    Source = "google",
                    ExternalId = e.Id,
                    CalendarName = "primary",
                    AllDay = e.Start?.DateTimeDateTimeOffset is null
                };

                await repo.UpsertCalendarEventAsync(calEvent);
                count++;
            }

            // Persist updated refresh token if it changed.
            var updatedJson = JsonSerializer.Serialize(credential.Token);
            await File.WriteAllTextAsync(TokenFile, updatedJson, ct);

            logger.LogInformation("Synced {count} events from Google Calendar.", count);
            return count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google Calendar sync failed");
            return 0;
        }
    }

    private GoogleAuthorizationCodeFlow BuildFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
        Scopes = Scopes
    });
}
