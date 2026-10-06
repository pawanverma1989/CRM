namespace IdentityApi.Infrastructure.HealthChecks;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Settings;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

/// <summary>
/// <c>identity-outbox</c> (tag <c>events</c>, served at <c>/health/events</c> only): Degraded when the
/// oldest unpublished outbox row is older than <c>RabbitMq:OutboxLagWarningSeconds</c>, which means
/// the relay is not reaching the broker. Not part of <c>/health</c>: the broker is not a liveness
/// dependency (writes keep working while it is down).
/// </summary>
public class OutboxLagHealthCheck(
    IOutboxRepository outbox,
    IOptions<RabbitMqSettings> settings,
    TimeProvider time) : IHealthCheck
{
    public const string Name = "identity-outbox";
    public const string Tag = "events";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var threshold = Math.Max(1, settings.Value.OutboxLagWarningSeconds);
        var backlog = await outbox.GetBacklogAsync(cancellationToken);

        var ageSeconds = backlog.OldestOccurredAt is { } oldest
            ? Math.Max(0L, (long)(time.GetUtcNow() - oldest).TotalSeconds)
            : 0L;

        var data = new Dictionary<string, object>
        {
            ["oldest_age_seconds"] = ageSeconds,
            ["unpublished_count"] = backlog.UnpublishedCount,
            ["threshold_seconds"] = threshold
        };

        return ageSeconds > threshold
            ? HealthCheckResult.Degraded(
                $"Oldest unpublished outbox row is {ageSeconds}s old (threshold {threshold}s).", data: data)
            : HealthCheckResult.Healthy("Outbox relay is keeping up.", data);
    }
}
