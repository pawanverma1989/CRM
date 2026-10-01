namespace IdentityApi.Infrastructure.Repositories;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;

public class OutboxRepository(IdentityDbContext context) : IOutboxRepository
{
    public void Add(OutboxEvent evt)
        => context.OutboxEvents.Add(evt);
}
