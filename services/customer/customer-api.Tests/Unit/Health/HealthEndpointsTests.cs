namespace CustomerApi.Tests.Unit.Health;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CustomerApi.Infrastructure.Health;
using Xunit;

public class HealthEndpointsTests
{
    [Fact]
    public async Task WriteEventsResponse_WritesStatusAndEveryCheckWithData()
    {
        var report = new HealthReport(new Dictionary<string, HealthReportEntry>
        {
            ["customer-outbox"] = new(HealthStatus.Healthy, "outbox is empty", TimeSpan.Zero, null,
                new Dictionary<string, object> { ["unpublished_count"] = 0, ["oldest_age_seconds"] = 0d }),
            ["customer-dead-letters"] = new(HealthStatus.Degraded, "broker unreachable", TimeSpan.Zero,
                new InvalidOperationException("secret connection detail"),
                new Dictionary<string, object> { ["queue"] = "crm.customer.inbox.dead" })
        }, TimeSpan.Zero);

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await HealthEndpoints.WriteEventsResponseAsync(http, report);

        http.Response.ContentType.Should().StartWith("application/json");
        http.Response.Body.Position = 0;
        var text = await new StreamReader(http.Response.Body).ReadToEndAsync();
        text.Should().NotContain("secret connection detail");

        var json = JsonDocument.Parse(text).RootElement;
        json.GetProperty("status").GetString().Should().Be("Degraded");

        var checks = json.GetProperty("checks").EnumerateArray().ToList();
        checks.Should().HaveCount(2);

        var deadLetters = checks.Single(c => c.GetProperty("name").GetString() == "customer-dead-letters");
        deadLetters.GetProperty("status").GetString().Should().Be("Degraded");
        deadLetters.GetProperty("description").GetString().Should().Be("broker unreachable");
        deadLetters.GetProperty("data").GetProperty("queue").GetString().Should().Be("crm.customer.inbox.dead");

        var outbox = checks.Single(c => c.GetProperty("name").GetString() == "customer-outbox");
        outbox.GetProperty("data").GetProperty("unpublished_count").GetInt32().Should().Be(0);
    }
}
