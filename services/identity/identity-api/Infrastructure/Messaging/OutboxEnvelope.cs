namespace IdentityApi.Infrastructure.Messaging;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdentityApi.Domain.Entities;

/// <summary>
/// Builds the platform event envelope (docs/architecture.md §4.3) for an outbox row at publish time.
/// Identity stores only the bare payload in <c>outbox_events.payload</c>; the envelope fields come
/// from the row's columns, so legacy rows written before the relay existed publish correctly too:
/// <list type="bullet">
///   <item><c>event_id</c> = row id (stable across redeliveries, so consumers can deduplicate)</item>
///   <item><c>version</c> = <c>payload.version</c> when it is an integer, else 0</item>
///   <item><c>actor_id</c> = <c>payload.actor_id</c> when it is a UUID, else null</item>
///   <item><c>payload</c> = the stored JSON as an object (not a string)</item>
/// </list>
/// </summary>
public static class OutboxEnvelope
{
    public static string Build(OutboxEvent row)
    {
        var payload = ParsePayload(row.Payload);
        var payloadObject = payload as JsonObject;

        var envelope = new JsonObject
        {
            ["event_id"] = row.Id.ToString("D"),
            ["event_type"] = row.EventType,
            ["organization_id"] = row.OrganizationId.ToString("D"),
            ["aggregate_type"] = row.AggregateType,
            ["aggregate_id"] = row.AggregateId.ToString("D"),
            ["version"] = ReadVersion(payloadObject),
            ["occurred_at"] = row.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
            ["actor_id"] = ReadActorId(payloadObject),
            ["payload"] = payload
        };

        return envelope.ToJsonString();
    }

    private static JsonNode? ParsePayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        try { return JsonNode.Parse(raw); }
        catch (JsonException) { return JsonValue.Create(raw); }
    }

    private static int ReadVersion(JsonObject? payload)
        => payload?["version"] is JsonValue v
           && v.GetValueKind() == JsonValueKind.Number
           && v.TryGetValue<int>(out var n) ? n : 0;

    private static string? ReadActorId(JsonObject? payload)
        => payload?["actor_id"] is JsonValue v
           && v.GetValueKind() == JsonValueKind.String
           && Guid.TryParse(v.GetValue<string>(), out var id) ? id.ToString("D") : null;
}
