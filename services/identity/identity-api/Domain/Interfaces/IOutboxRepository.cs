namespace IdentityApi.Domain.Interfaces;
using IdentityApi.Domain.Entities;

public interface IOutboxRepository
{
    void Add(OutboxEvent evt);

    /// <summary>Unpublished row count and the oldest unpublished <c>occurred_at</c> (null when none).</summary>
    Task<OutboxBacklog> GetBacklogAsync(CancellationToken ct = default);
}

public sealed record OutboxBacklog(int UnpublishedCount, DateTimeOffset? OldestOccurredAt);
