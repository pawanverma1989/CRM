namespace LeadApi.Tests.Health;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using LeadApi.Infrastructure.Data;
using Xunit;

/// <summary>
/// Hosts the real pipeline with the database swapped for EF InMemory and the broker pointed at a
/// closed local port, so the dead-letter check always sees "broker unreachable".
/// </summary>
public sealed class LeadHealthFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workers:OutboxRelayEnabled"] = "false",
            ["Workers:ConsumerEnabled"] = "false",
            ["Workers:ConversionRetryEnabled"] = "false",
            ["Workers:PurgeEnabled"] = "false",
            ["RabbitMq:Host"] = "127.0.0.1",
            ["RabbitMq:Port"] = "1",
            ["RabbitMq:Queue"] = "crm.lead.inbox"
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<LeadDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<LeadDbContext>>();
            services.AddDbContext<LeadDbContext>(o => o.UseInMemoryDatabase(_databaseName));

            // The database check needs a real PostgreSQL; drop it so /health only shows what remains.
            services.Configure<HealthCheckServiceOptions>(o =>
            {
                var db = o.Registrations.FirstOrDefault(r => r.Name == "lead-db");
                if (db is not null) o.Registrations.Remove(db);
            });
        });
    }
}

public class EventHealthEndpointTests(LeadHealthFactory factory) : IClassFixture<LeadHealthFactory>
{
    [Fact]
    public async Task GetHealth_ExcludesEventChecks_SoBrokerOutageDoesNotChangeIt()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task GetHealthEvents_WhenBrokerIsUnreachable_Returns503WithJsonBody()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/events");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        json.GetProperty("status").GetString().Should().Be("Degraded");

        var checks = json.GetProperty("checks").EnumerateArray().ToList();
        checks.Select(c => c.GetProperty("name").GetString())
              .Should().BeEquivalentTo("lead-outbox", "lead-dead-letters");

        var deadLetters = checks.Single(c => c.GetProperty("name").GetString() == "lead-dead-letters");
        deadLetters.GetProperty("status").GetString().Should().Be("Degraded");
        deadLetters.GetProperty("description").GetString().Should().Be("broker unreachable");
        deadLetters.GetProperty("data").GetProperty("queue").GetString().Should().Be("crm.lead.inbox.dead");

        var outbox = checks.Single(c => c.GetProperty("name").GetString() == "lead-outbox");
        outbox.GetProperty("status").GetString().Should().Be("Healthy");
        outbox.GetProperty("data").GetProperty("unpublished_count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GetHealthEvents_DoesNotRequireAuthentication()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/events");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
