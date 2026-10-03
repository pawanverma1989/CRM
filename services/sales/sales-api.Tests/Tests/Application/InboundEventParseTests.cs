namespace SalesApi.Tests.Application;

using System;
using System.Text.Json;
using FluentAssertions;
using SalesApi.Application.Events;
using Xunit;

public class InboundEventParseTests
{
    private static string BuildEnvelope(
        string? eventType = "contact.created",
        Guid? eventId = null,
        Guid? organizationId = null,
        Guid? aggregateId = null,
        int? version = 1,
        object? payload = null)
    {
        var obj = new System.Collections.Generic.Dictionary<string, object?>();

        if (eventType is not null) obj["event_type"] = eventType;
        if (eventId.HasValue) obj["event_id"] = eventId.Value;
        if (organizationId.HasValue) obj["organization_id"] = organizationId.Value;
        if (aggregateId.HasValue) obj["aggregate_id"] = aggregateId.Value;
        if (version.HasValue) obj["version"] = version.Value;
        if (payload is not null) obj["payload"] = payload;

        return JsonSerializer.Serialize(obj);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenBodyIsEmpty()
    {
        var result = InboundEvent.Parse(string.Empty, routingKey: null);
        result.Should().BeNull();
    }

    [Fact]
    public void Parse_ReturnsNull_WhenBodyIsInvalidJson()
    {
        var result = InboundEvent.Parse("not-json-at-all{{{", routingKey: null);
        result.Should().BeNull();
    }

    [Fact]
    public void Parse_ParsesEventType_FromEnvelopeField()
    {
        var body = BuildEnvelope(eventType: "deal.stage_changed");
        var result = InboundEvent.Parse(body, routingKey: null);

        result.Should().NotBeNull();
        result!.EventType.Should().Be("deal.stage_changed");
    }

    [Fact]
    public void Parse_FallsBackToRoutingKey_WhenNoEventTypeField()
    {
        // Build a JSON body WITHOUT an event_type field
        var obj = new { organization_id = Guid.NewGuid(), aggregate_id = Guid.NewGuid(), version = 1 };
        var body = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        var result = InboundEvent.Parse(body, routingKey: "user.updated");

        result.Should().NotBeNull();
        result!.EventType.Should().Be("user.updated");
    }

    [Fact]
    public void Parse_ExtractsOrganizationIdFromPayload_WhenNotInRoot()
    {
        var orgId = Guid.NewGuid();
        // Root has no organization_id; payload contains it
        var body = JsonSerializer.Serialize(new
        {
            event_type = "company.created",
            aggregate_id = Guid.NewGuid(),
            version = 1,
            payload = new { organization_id = orgId, name = "Acme" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var result = InboundEvent.Parse(body, routingKey: null);

        result.Should().NotBeNull();
        result!.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public void Parse_GeneratesNewEventId_WhenMissing()
    {
        // Build envelope without event_id field
        var obj = new { event_type = "contact.created", organization_id = Guid.NewGuid(), version = 1 };
        var body = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        var result = InboundEvent.Parse(body, routingKey: null);

        result.Should().NotBeNull();
        result!.EventId.Should().NotBe(Guid.Empty);
    }
}
