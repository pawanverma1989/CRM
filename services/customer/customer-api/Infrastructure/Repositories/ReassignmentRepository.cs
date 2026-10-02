namespace CustomerApi.Infrastructure.Repositories;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The reassignment queue (OWN-3) and the local <c>user_refs</c> copy, both maintained from
/// identity events. <c>uq_reassignment_open</c> is what makes a redelivered
/// <c>user.deactivated</c> a no-op (AC-12).
/// </summary>
public interface IReassignmentRepository
{
    Task<List<ReassignmentQueueEntry>> OpenEntriesAsync(Guid organizationId, CancellationToken ct);

    Task<List<ReassignmentQueueEntry>> OpenEntriesForRecordsAsync(
        Guid organizationId, string recordType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct);

    void Add(ReassignmentQueueEntry entry);

    Task<UserRef?> UserRefAsync(Guid userId, CancellationToken ct);

    void AddUserRef(UserRef userRef);
}

public class ReassignmentRepository(CustomerDbContext context) : IReassignmentRepository
{
    public Task<List<ReassignmentQueueEntry>> OpenEntriesAsync(Guid organizationId, CancellationToken ct)
        => context.ReassignmentQueue
            .Where(e => e.OrganizationId == organizationId && e.ResolvedAt == null)
            .OrderBy(e => e.QueuedAt)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<ReassignmentQueueEntry>> OpenEntriesForRecordsAsync(
        Guid organizationId, string recordType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct)
    {
        var ids = recordIds.Distinct().ToArray();
        return context.ReassignmentQueue
            .Where(e => e.OrganizationId == organizationId
                     && e.RecordType == recordType
                     && e.ResolvedAt == null
                     && ids.Contains(e.RecordId))
            .ToListAsync(ct);
    }

    public void Add(ReassignmentQueueEntry entry) => context.ReassignmentQueue.Add(entry);

    public Task<UserRef?> UserRefAsync(Guid userId, CancellationToken ct)
        => context.UserRefs.FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public void AddUserRef(UserRef userRef) => context.UserRefs.Add(userRef);
}
