namespace CustomerApi.Tests.Integration.Health;
using System.Net;
using System.Text.Json;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Health;
using CustomerApi.Settings;
using CustomerApi.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

/// <summary>
/// The outbox lag check against the real customer_db (Testcontainers), and the two health
/// endpoints through the real host. The fixture points the broker at a closed local port.
/// </summary>
public class EventHealthTests(CustomerApiFixture fixture) : ApiTestBase(fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private Task InsertOutboxRowAsync(TimeSpan age, bool published = false)
        => Fixture.ExecuteAsync(
            """
            INSERT INTO outbox_events (organization_id, aggregate_type, aggregate_id, event_type, payload, occurred_at, published_at)
            VALUES (@org, 'contact', @aggregate, 'contact.updated', '{}'::jsonb, @occurred, @published)
            """,
            ("org", OrganizationId),
            ("aggregate", Guid.NewGuid()),
            ("occurred", Now - age),
            ("published", published ? (object)Now : DBNull.Value));

    private async Task<HealthCheckResult> RunOutboxCheckAsync(int thresholdSeconds = 60)
    {
        using var scope = Fixture.CreateScope();
        var check = new OutboxHealthCheck(
            scope.ServiceProvider.GetRequiredService<CustomerDbContext>(),
            Options.Create(new RabbitMqSettings { OutboxLagWarningSeconds = thresholdSeconds }),
            new FixedTimeProvider(Now));

        return await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    [Fact]
    public async Task OutboxCheck_WhenNothingIsUnpublished_ReturnsHealthy()
    {
        await InsertOutboxRowAsync(TimeSpan.FromHours(2), published: true);

        var result = await RunOutboxCheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(0);
        result.Data["oldest_age_seconds"].Should().Be(0d);
    }

    [Fact]
    public async Task OutboxCheck_WhenOldestUnpublishedRowIsWithinThreshold_ReturnsHealthy()
    {
        await InsertOutboxRowAsync(TimeSpan.FromSeconds(45));
        await InsertOutboxRowAsync(TimeSpan.FromSeconds(5));

        var result = await RunOutboxCheckAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["unpublished_count"].Should().Be(2);
        result.Data["oldest_age_seconds"].Should().Be(45d);
    }

    [Fact]
    public async Task OutboxCheck_WhenOldestUnpublishedRowIsOlderThanThreshold_ReturnsDegraded()
    {
        await InsertOutboxRowAsync(TimeSpan.FromSeconds(120));
        await InsertOutboxRowAsync(TimeSpan.FromSeconds(10));
        await InsertOutboxRowAsync(TimeSpan.FromHours(5), published: true);

        var result = await RunOutboxCheckAsync();

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data["unpublished_count"].Should().Be(2);
        result.Data["oldest_age_seconds"].Should().Be(120d);
    }

    [Fact]
    public async Task GetHealth_StaysHealthyWithTheBrokerDown_BecauseEventChecksAreExcluded()
    {
        var response = await Anonymous().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task GetHealthEvents_WhenBrokerIsUnreachable_Returns503WithJsonBody()
    {
        var response = await Anonymous().GetAsync("/health/events");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("status").GetString().Should().Be("Degraded");

        var checks = json.GetProperty("checks").EnumerateArray().ToList();
        checks.Select(c => c.GetProperty("name").GetString())
              .Should().BeEquivalentTo("customer-outbox", "customer-dead-letters");

        var deadLetters = checks.Single(c => c.GetProperty("name").GetString() == "customer-dead-letters");
        deadLetters.GetProperty("status").GetString().Should().Be("Degraded");
        deadLetters.GetProperty("description").GetString().Should().Be("broker unreachable");
        deadLetters.GetProperty("data").GetProperty("queue").GetString().Should().Be("crm.customer.inbox.dead");

        var outbox = checks.Single(c => c.GetProperty("name").GetString() == "customer-outbox");
        outbox.GetProperty("status").GetString().Should().Be("Healthy");
        outbox.GetProperty("data").GetProperty("unpublished_count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetHealthEvents_WithALaggingOutbox_ReportsOutboxDegraded()
    {
        await Fixture.ExecuteAsync(
            """
            INSERT INTO outbox_events (organization_id, aggregate_type, aggregate_id, event_type, payload, occurred_at)
            VALUES (@org, 'contact', @aggregate, 'contact.updated', '{}'::jsonb, now() - interval '10 minutes')
            """,
            ("org", OrganizationId), ("aggregate", Guid.NewGuid()));

        var response = await Anonymous().GetAsync("/health/events");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var outbox = json.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "customer-outbox");
        outbox.GetProperty("status").GetString().Should().Be("Degraded");
        outbox.GetProperty("data").GetProperty("unpublished_count").GetInt32().Should().Be(1);
        outbox.GetProperty("data").GetProperty("oldest_age_seconds").GetDouble().Should().BeGreaterThan(60);
    }
}
