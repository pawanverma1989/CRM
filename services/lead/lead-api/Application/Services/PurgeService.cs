namespace LeadApi.Application.Services;
using LeadApi.Application.Events;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Hard-deletes leads whose <c>deleted_at</c> is older than the retention window (DEL-3).
/// Publishes a <c>lead.purged</c> event (no personal data in payload) per deleted lead.
/// </summary>
public interface IPurgeService
{
    Task PurgeAsync(CancellationToken ct);
}

public class PurgeService(
    LeadDbContext context,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<LeadSettings> settings,
    ILogger<PurgeService> logger) : IPurgeService
{
    private readonly LeadSettings _settings = settings.Value;

    public async Task PurgeAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-_settings.RecycleBinRetentionDays);

        var toDelete = await context.Leads
            .Where(l => l.DeletedAt != null && l.DeletedAt < cutoff)
            .ToListAsync(ct);

        if (toDelete.Count == 0)
        {
            logger.LogDebug("Purge: nothing to delete.");
            return;
        }

        foreach (var lead in toDelete)
        {
            // Hard-delete web form submissions
            var submissions = await context.WebFormSubmissions
                .Where(s => s.LeadId == lead.Id)
                .ToListAsync(ct);
            context.WebFormSubmissions.RemoveRange(submissions);

            // Hard-delete conversions
            var conversions = await context.LeadConversions
                .Where(c => c.LeadId == lead.Id)
                .ToListAsync(ct);
            context.LeadConversions.RemoveRange(conversions);

            context.Leads.Remove(lead);

            // No personal data in the purge event.
            outbox.Add(EventTypes.LeadPurged, AggregateTypes.Lead, lead.Id,
                lead.OrganizationId, lead.Version, null,
                new { lead_id = lead.Id });
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Purge: hard-deleted {Count} lead(s) deleted before {Cutoff}.",
            toDelete.Count, cutoff);
    }
}
