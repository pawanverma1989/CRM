namespace SalesApi.Tests.Health;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Health;
using SalesApi.Settings;
using SalesApi.Tests.TestSupport;
using Xunit;

public class OutboxHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static SalesDbContext CreateContext()
        => new(new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static OutboxHealthCheck CreateCheck(SalesDbContext db, int thresholdSeconds = 60)
        => new(db,
            Options.Create(new RabbitMqSettings { OutboxLagWarningSeconds = thresholdSeconds }),
            new FixedTimeProvider(Now));

    private static OutboxEvent Row(TimeSpan age, bool published = false) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        AggregateType = "deal",
        AggregateId = Guid.NewGuid(),
        EventType = "deal.updated",
        Payload = "{}",
        OccurredAt = Now - age,
        PublishedAt = published ? Now : null
    };

    private static Task<HealthCheckResult> RunAsync(OutboxHealthCheck check)
        => check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    [Fact]
    public async Task CheckHealth_WhenOutboxIsEmpty_ReturnsHealthyWithZeroCounts()
    {
        await using var db = CreateContext();

        var result = await RunAsync(CreateCheck(db));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(0);
        result.Data["oldest_age_seconds"].Should().Be(0d);
    }

    [Fact]
    public async Task CheckHealth_WhenOldestUnpublishedRowIsWithinThreshold_ReturnsHealthy()
    {
        await using var db = CreateContext();
        db.OutboxEvents.AddRange(Row(TimeSpan.FromSeconds(30)), Row(TimeSpan.FromSeconds(5)));
        await db.SaveChangesAsync();

        var result = await RunAsync(CreateCheck(db));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(2);
        result.Data["oldest_age_seconds"].Should().Be(30d);
    }

    [Fact]
    public async Task CheckHealth_WhenOldestUnpublishedRowIsOlderThanThreshold_ReturnsDegraded()
    {
        await using var db = CreateContext();
        db.OutboxEvents.AddRange(
            Row(TimeSpan.FromSeconds(90)),
            Row(TimeSpan.FromSeconds(10)),
            Row(TimeSpan.FromHours(1), published: true));
        await db.SaveChangesAsync();

        var result = await RunAsync(CreateCheck(db));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["unpublished_count"].Should().Be(2);
        result.Data["oldest_age_seconds"].Should().Be(90d);
    }

    [Fact]
    public async Task CheckHealth_WhenOnlyPublishedRowsAreOld_ReturnsHealthy()
    {
        await using var db = CreateContext();
        db.OutboxEvents.Add(Row(TimeSpan.FromHours(3), published: true));
        await db.SaveChangesAsync();

        var result = await RunAsync(CreateCheck(db));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealth_UsesConfiguredThreshold()
    {
        await using var db = CreateContext();
        db.OutboxEvents.Add(Row(TimeSpan.FromSeconds(20)));
        await db.SaveChangesAsync();

        var result = await RunAsync(CreateCheck(db, thresholdSeconds: 15));

        result.Status.Should().Be(HealthStatus.Degraded);
    }
}
