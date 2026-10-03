namespace SalesApi.Application.Services;
using SalesApi.Application.DTOs;
using SalesApi.Infrastructure.Repositories;

/// <summary>
/// Manages the reassignment queue — open deals of deactivated users that need manual reassignment (OWN-2).
/// </summary>
public interface IReassignmentService
{
    Task<List<ReassignmentQueueItemDto>> GetQueueAsync(CancellationToken ct);
}

public sealed record ReassignmentQueueItemDto(
    Guid QueueId,
    Guid DealId,
    string DealName,
    Guid DeactivatedUserId,
    DateTimeOffset QueuedAt);

public class ReassignmentService(
    IDealRepository deals,
    IRequestContext ctx) : IReassignmentService
{
    public async Task<List<ReassignmentQueueItemDto>> GetQueueAsync(CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may view the reassignment queue.");

        var queue = await deals.GetReassignmentQueueAsync(ctx.OrganizationId, ct);

        return [.. queue.Select(r => new ReassignmentQueueItemDto(
            r.Id,
            r.DealId,
            r.Deal?.Name ?? r.DealId.ToString(),
            r.DeactivatedUserId,
            r.QueuedAt))];
    }
}
