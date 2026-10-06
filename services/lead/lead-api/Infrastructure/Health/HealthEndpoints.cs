namespace LeadApi.Infrastructure.Health;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Health endpoints. <c>/health</c> is what the container healthcheck calls and stays exactly as
/// before (database only, NFR-9: the broker is not a liveness dependency). <c>/health/events</c>
/// reports the event pipeline (outbox lag, dead letters) and returns 503 when it is not Healthy.
/// </summary>
public static class HealthEndpoints
{
    public const string EventsTag = "events";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static IHealthChecksBuilder AddEventPipelineChecks(this IHealthChecksBuilder builder, string service)
        => builder
            .AddCheck<OutboxHealthCheck>($"{service}-outbox", tags: [EventsTag])
            .AddCheck<DeadLetterHealthCheck>($"{service}-dead-letters", tags: [EventsTag]);

    public static IEndpointRouteBuilder MapServiceHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => !registration.Tags.Contains(EventsTag)
        });

        endpoints.MapHealthChecks("/health/events", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(EventsTag),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = WriteEventsResponseAsync
        });

        return endpoints;
    }

    /// <summary>Status plus name/status/description/data per check. Exceptions are never written.</summary>
    public static Task WriteEventsResponseAsync(HttpContext http, HealthReport report)
    {
        http.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                data = entry.Value.Data
            })
        };

        return http.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
