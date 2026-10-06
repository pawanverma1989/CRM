namespace IdentityApi.Tests.Infrastructure;
using FluentAssertions;
using IdentityApi.Domain.Entities;
using IdentityApi.Infrastructure.HealthChecks;
using IdentityApi.Infrastructure.Repositories;
using IdentityApi.Settings;
using IdentityApi.Tests.Helpers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

public class OutboxLagHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static OutboxEvent Row(DateTimeOffset occurredAt, DateTimeOffset? publishedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        AggregateType = "user",
        AggregateId = Guid.NewGuid(),
        EventType = "user.updated",
        Payload = "{}",
        OccurredAt = occurredAt,
        PublishedAt = publishedAt
    };

    private static async Task<HealthCheckResult> RunAsync(params OutboxEvent[] rows)
    {
        var context = DbContextFactory.Create();
        context.OutboxEvents.AddRange(rows);
        await context.SaveChangesAsync();

        var check = new OutboxLagHealthCheck(
            new OutboxRepository(context),
            Options.Create(new RabbitMqSettings { OutboxLagWarningSeconds = 60 }),
            new FixedTime(Now));

        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    [Fact]
    public async Task CheckHealth_NoRows_ReturnsHealthy()
    {
        var result = await RunAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealth_FreshUnpublishedRow_ReturnsHealthy()
    {
        var result = await RunAsync(Row(Now.AddSeconds(-10)));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealth_OldPublishedRow_ReturnsHealthy()
    {
        var result = await RunAsync(Row(Now.AddHours(-2), publishedAt: Now.AddHours(-2)));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealth_OldUnpublishedRow_ReturnsDegradedWithAgeAndCount()
    {
        var result = await RunAsync(Row(Now.AddSeconds(-300)), Row(Now.AddSeconds(-5)));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["oldest_age_seconds"].Should().Be(300L);
        result.Data["unpublished_count"].Should().Be(2);
    }
}
