namespace CustomerApi.Infrastructure.Health;
using CustomerApi.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;

/// <summary>
/// <c>customer-outbox</c> (tag <c>events</c>): Degraded when the oldest unpublished
/// <c>outbox_events</c> row is older than <see cref="RabbitMqSettings.OutboxLagWarningSeconds"/>.
/// </summary>
public class OutboxHealthCheck(
    CustomerDbContext db,
    IOptions<RabbitMqSettings> rabbit,
    TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var threshold = rabbit.Value.OutboxLagWarningThreshold;

        OutboxLag lag;
        try
        {
            lag = await OutboxLag.MeasureAsync(db.OutboxEvents, clock.GetUtcNow(), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("outbox could not be read");
        }

        var data = new Dictionary<string, object>
        {
            ["oldest_age_seconds"] = lag.OldestAgeSeconds,
            ["unpublished_count"] = lag.UnpublishedCount,
            ["threshold_seconds"] = threshold.TotalSeconds
        };

        if (lag.UnpublishedCount > 0 && lag.OldestAge > threshold)
            return HealthCheckResult.Degraded(
                $"oldest unpublished outbox row is {lag.OldestAgeSeconds}s old (threshold {threshold.TotalSeconds}s)",
                data: data);

        return HealthCheckResult.Healthy(
            lag.UnpublishedCount == 0 ? "outbox is empty" : "outbox is within the lag threshold",
            data);
    }
}
