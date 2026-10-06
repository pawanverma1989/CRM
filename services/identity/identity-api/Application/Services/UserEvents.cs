namespace IdentityApi.Application.Services;
using System.Text.Json;
using IdentityApi.Domain.Entities;

/// <summary>
/// Single place that shapes user.* outbox payloads. Every payload carries the user's <c>version</c>
/// (V4) and <c>actor_id</c>; the relay lifts both into the envelope. Never put the password or its
/// hash here.
/// </summary>
public static class UserEvents
{
    public const string AggregateType = "user";

    /// <summary>The user's current state — the body of user.updated / deactivated / reactivated.</summary>
    public static Dictionary<string, object?> StatePayload(User user, Guid? actorId) => new()
    {
        ["user_id"] = user.Id,
        ["email"] = user.Email,
        ["first_name"] = user.FirstName,
        ["last_name"] = user.LastName,
        ["role"] = user.Role,
        ["team_id"] = user.TeamId,
        ["status"] = user.Status,
        ["version"] = user.Version,
        ["actor_id"] = actorId
    };

    public static OutboxEvent State(User user, string eventType, Guid? actorId)
        => Create(user, eventType, StatePayload(user, actorId));

    /// <summary>user.created, shared by every creation path.</summary>
    public static OutboxEvent Created(User user, Guid? createdBy, string? creationMethod)
    {
        var payload = StatePayload(user, createdBy ?? user.Id);
        payload["created_by"] = createdBy;
        payload["creation_method"] = creationMethod;
        return Create(user, "user.created", payload);
    }

    public static OutboxEvent Create(User user, string eventType, object payload) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = user.OrganizationId,
        AggregateType = AggregateType,
        AggregateId = user.Id,
        EventType = eventType,
        Payload = JsonSerializer.Serialize(payload),
        OccurredAt = DateTimeOffset.UtcNow
    };
}
