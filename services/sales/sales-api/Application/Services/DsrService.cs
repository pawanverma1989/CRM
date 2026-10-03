namespace SalesApi.Application.Services;
using SalesApi.Application.Events;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Handles GDPR/DPDP data subject requests for personal data erasure (DSR-1).
/// Hard-deletes deal data referencing the subject, then publishes dsr.erasure_completed.
/// </summary>
public interface IDsrService
{
    Task EraseAsync(Guid dsrId, Guid organizationId, IReadOnlyList<Guid> contactIds, CancellationToken ct);
}

public class DsrService(
    SalesDbContext context,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    ILogger<DsrService> logger) : IDsrService
{
    public async Task EraseAsync(Guid dsrId, Guid organizationId, IReadOnlyList<Guid> contactIds, CancellationToken ct)
    {
        var erasedCount = 0;

        // Remove deal_contacts referencing these contacts
        if (contactIds.Count > 0)
        {
            var affectedContacts = await context.DealContacts
                .Where(dc => contactIds.Contains(dc.ContactId))
                .ToListAsync(ct);
            context.DealContacts.RemoveRange(affectedContacts);
            erasedCount += affectedContacts.Count;

            // Also clear primary_contact_id on deals
            var affectedDeals = await context.Deals
                .Where(d => d.OrganizationId == organizationId
                         && d.PrimaryContactId.HasValue
                         && contactIds.Contains(d.PrimaryContactId.Value))
                .ToListAsync(ct);

            foreach (var deal in affectedDeals)
                deal.PrimaryContactId = null;

            // Remove customer_refs
            var contactRefs = await context.CustomerRefs
                .Where(r => r.EntityType == "contact" && contactIds.Contains(r.Id)
                         && r.OrganizationId == organizationId)
                .ToListAsync(ct);
            context.CustomerRefs.RemoveRange(contactRefs);
        }

        outbox.Add(EventTypes.DsrErasureCompleted, AggregateTypes.Dsr, dsrId,
            organizationId, 1, null,
            new { dsr_id = dsrId, erased_count = erasedCount });

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("DSR erasure {DsrId}: erased {Count} contact reference(s) in org {OrgId}.",
            dsrId, erasedCount, organizationId);
    }
}
