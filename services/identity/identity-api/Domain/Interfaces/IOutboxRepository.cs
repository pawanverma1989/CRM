namespace IdentityApi.Domain.Interfaces;
using IdentityApi.Domain.Entities;

public interface IOutboxRepository
{
    void Add(OutboxEvent evt);
}
