namespace SalesApi.Infrastructure.Repositories;
using SalesApi.Application.Events;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;

/// <summary>
/// Appends outbox rows to the same <see cref="SalesDbContext"/> change tracker as the data
/// change, so the row and the event are committed by one <c>SaveChangesAsync</c>
/// (CLAUDE.md rule 3, NFR-5). Nothing here ever touches the broker.
/// </summary>
public interface IOutboxWriter
{
    OutboxEvent Add(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        Guid organizationId,
        int version,
        Guid? actorId,
        object? payload);
}

public class OutboxWriter(SalesDbContext context, TimeProvider clock) : IOutboxWriter
{
    public OutboxEvent Add(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        Guid organizationId,
        int version,
        Guid? actorId,
        object? payload)
    {
        var id = Guid.NewGuid();
        var occurredAt = clock.GetUtcNow();

        var envelope = new EventEnvelope(
            id, eventType, organizationId, aggregateType, aggregateId, version, occurredAt, actorId, payload);

        var row = new OutboxEvent
        {
            Id = id,
            OrganizationId = organizationId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            Payload = SalesJson.SerializeEvent(envelope),
            OccurredAt = occurredAt,
            PublishAttempts = 0
        };

        context.OutboxEvents.Add(row);
        return row;
    }
}
