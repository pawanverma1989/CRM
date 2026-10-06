namespace IdentityApi.Tests.Integration.Api;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using IdentityApi.Domain.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

/// <summary><c>GET /health/events</c>: anonymous, JSON body, Degraded → 503 so monitoring can alert.</summary>
public class HealthEventsEndpointTests : IClassFixture<HealthEventsEndpointTests.Factory>
{
    private readonly Factory _factory;

    public HealthEventsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Outbox.Reset();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public Mock<IOutboxRepository> Outbox { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Workers:OutboxRelayEnabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxRepository>();
                services.AddSingleton(Outbox.Object);
            });
        }
    }

    [Fact]
    public async Task HealthEvents_FreshBacklog_Returns200WithOutboxCheck()
    {
        _factory.Outbox.Setup(o => o.GetBacklogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboxBacklog(0, null));

        var response = await _factory.CreateClient().GetAsync("/health/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        var check = doc.RootElement.GetProperty("checks").EnumerateArray().Should().ContainSingle().Subject;
        check.GetProperty("name").GetString().Should().Be("identity-outbox");
        check.GetProperty("data").GetProperty("unpublished_count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task HealthEvents_OldUnpublishedRow_Returns503Degraded()
    {
        _factory.Outbox.Setup(o => o.GetBacklogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboxBacklog(28, DateTimeOffset.UtcNow.AddHours(-3)));

        var response = await _factory.CreateClient().GetAsync("/health/events");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Degraded");
        var check = doc.RootElement.GetProperty("checks")[0];
        check.GetProperty("status").GetString().Should().Be("Degraded");
        check.GetProperty("description").GetString().Should().NotBeNullOrEmpty();
        check.GetProperty("data").GetProperty("unpublished_count").GetInt32().Should().Be(28);
        check.GetProperty("data").GetProperty("oldest_age_seconds").GetInt64().Should().BeGreaterThan(10_000);
    }
}
