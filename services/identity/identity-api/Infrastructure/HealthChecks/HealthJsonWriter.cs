namespace IdentityApi.Infrastructure.HealthChecks;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>JSON body for <c>/health/events</c>: overall status plus each check's status, description and data.</summary>
public static class HealthJsonWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            status = report.Status.ToString(),
            total_duration_ms = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Options));
    }
}
