namespace SalesApi.Application.Services;
using SalesApi.Application.Events;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Hard-deletes deals whose <c>deleted_at</c> is older than the retention window (DEL-1).
/// Publishes a <c>deal.purged</c> event (no personal data in payload) per deleted deal.
/// </summary>
public interface IPurgeService
{
    Task PurgeAsync(CancellationToken ct);
}

public class PurgeService(
    SalesDbContext context,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<SalesSettings> settings,
    ILogger<PurgeService> logger) : IPurgeService
{
    private readonly SalesSettings _settings = settings.Value;

    public async Task PurgeAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-_settings.RecycleBinRetentionDays);

        var toDelete = await context.Deals
            .Where(d => d.DeletedAt != null && d.DeletedAt < cutoff)
            .ToListAsync(ct);

        if (toDelete.Count == 0)
        {
            logger.LogDebug("Purge: nothing to delete.");
            return;
        }

        foreach (var deal in toDelete)
        {
            // Cascade deletes deal_contacts, deal_stage_history, reassignment_queue via FK
            context.Deals.Remove(deal);

            // No personal data in purge event
            outbox.Add(EventTypes.DealPurged, AggregateTypes.Deal, deal.Id,
                deal.OrganizationId, deal.Version, null,
                new { deal_id = deal.Id });
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Purge: hard-deleted {Count} deal(s) deleted before {Cutoff}.",
            toDelete.Count, cutoff);
    }
}
