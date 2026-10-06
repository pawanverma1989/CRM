namespace IdentityApi.Tests.Infrastructure;
using System.Text.Json;
using FluentAssertions;
using IdentityApi.Domain.Entities;
using IdentityApi.Infrastructure.Messaging;

public class OutboxEnvelopeTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 6, 9, 15, 0, TimeSpan.Zero);

    private static OutboxEvent Row(string payload, string eventType = "user.updated") => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        OrganizationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        AggregateType = "user",
        AggregateId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        EventType = eventType,
        Payload = payload,
        OccurredAt = OccurredAt
    };

    [Fact]
    public void Build_LegacyBarePayload_WrapsInEnvelopeWithRowColumnsAndVersionZero()
    {
        var row = Row("""{"user_id":"33333333-3333-3333-3333-333333333333","email":"a@b.c","role":"admin"}""");

        using var doc = JsonDocument.Parse(OutboxEnvelope.Build(row));
        var e = doc.RootElement;

        e.EnumerateObject().Select(p => p.Name).Should().Equal(
            "event_id", "event_type", "organization_id", "aggregate_type", "aggregate_id",
            "version", "occurred_at", "actor_id", "payload");
        e.GetProperty("event_id").GetGuid().Should().Be(row.Id);
        e.GetProperty("event_type").GetString().Should().Be("user.updated");
        e.GetProperty("organization_id").GetGuid().Should().Be(row.OrganizationId);
        e.GetProperty("aggregate_type").GetString().Should().Be("user");
        e.GetProperty("aggregate_id").GetGuid().Should().Be(row.AggregateId);
        e.GetProperty("version").GetInt32().Should().Be(0);
        e.GetProperty("occurred_at").GetDateTimeOffset().Should().Be(OccurredAt);
        e.GetProperty("actor_id").ValueKind.Should().Be(JsonValueKind.Null);
        e.GetProperty("payload").ValueKind.Should().Be(JsonValueKind.Object);
        e.GetProperty("payload").GetProperty("role").GetString().Should().Be("admin");
    }

    [Fact]
    public void Build_PayloadWithVersionAndActor_CopiesThemIntoEnvelope()
    {
        var actor = Guid.NewGuid();
        var row = Row($$"""{"user_id":"33333333-3333-3333-3333-333333333333","version":7,"actor_id":"{{actor}}"}""");

        using var doc = JsonDocument.Parse(OutboxEnvelope.Build(row));
        var e = doc.RootElement;

        e.GetProperty("version").GetInt32().Should().Be(7);
        e.GetProperty("actor_id").GetGuid().Should().Be(actor);
        // The stored payload is kept intact (as an object, not a string).
        e.GetProperty("payload").GetProperty("version").GetInt32().Should().Be(7);
        e.GetProperty("payload").GetProperty("actor_id").GetGuid().Should().Be(actor);
    }

    [Fact]
    public void Build_NonGuidActorOrNonNumericVersion_FallsBackToDefaults()
    {
        var row = Row("""{"version":"seven","actor_id":"not-a-guid"}""");

        using var doc = JsonDocument.Parse(OutboxEnvelope.Build(row));

        doc.RootElement.GetProperty("version").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("actor_id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Build_SameRowTwice_ProducesSameEventId()
    {
        // Redelivery after a failed confirm must carry the same event_id so consumers can deduplicate.
        var row = Row("""{"user_id":"33333333-3333-3333-3333-333333333333"}""");

        using var first = JsonDocument.Parse(OutboxEnvelope.Build(row));
        using var second = JsonDocument.Parse(OutboxEnvelope.Build(row));

        first.RootElement.GetProperty("event_id").GetGuid()
            .Should().Be(second.RootElement.GetProperty("event_id").GetGuid());
    }

    [Fact]
    public void Build_ParsesWithConsumerContract()
    {
        // Mirrors InboundEvent.Parse in the consumer services: snake_case envelope fields at the root.
        var row = Row("""{"user_id":"33333333-3333-3333-3333-333333333333","version":3}""", "user.created");

        using var doc = JsonDocument.Parse(OutboxEnvelope.Build(row));
        var e = doc.RootElement;

        e.GetProperty("event_type").GetString().Should().Be("user.created");
        e.GetProperty("organization_id").GetString().Should().Be("22222222-2222-2222-2222-222222222222");
        e.GetProperty("payload").GetProperty("user_id").GetString().Should().Be("33333333-3333-3333-3333-333333333333");
        e.GetProperty("version").GetInt32().Should().Be(3);
    }
}
