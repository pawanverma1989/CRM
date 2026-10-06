namespace IdentityApi.Infrastructure.Repositories;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class OutboxRepository(IdentityDbContext context) : IOutboxRepository
{
    public void Add(OutboxEvent evt)
        => context.OutboxEvents.Add(evt);

    // Both queries are served by idx_outbox_unpublished (occurred_at) WHERE published_at IS NULL.
    public async Task<OutboxBacklog> GetBacklogAsync(CancellationToken ct = default)
    {
        var unpublished = context.OutboxEvents.AsNoTracking().Where(e => e.PublishedAt == null);
        var count = await unpublished.CountAsync(ct);
        var oldest = count == 0
            ? null
            : await unpublished.OrderBy(e => e.OccurredAt).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefaultAsync(ct);
        return new OutboxBacklog(count, oldest);
    }
}
