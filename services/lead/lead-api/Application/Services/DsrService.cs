namespace LeadApi.Application.Services;
using LeadApi.Application.Events;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Handles GDPR/DPDP data subject requests for personal data erasure (DSR-1).
/// Hard-deletes all personal data for the specified emails/lead IDs, including
/// web form submissions, then publishes dsr.erasure_completed.
/// </summary>
public interface IDsrService
{
    Task EraseAsync(Guid dsrId, Guid organizationId, IReadOnlyList<string> emails, IReadOnlyList<Guid> leadIds, CancellationToken ct);
}

public class DsrService(
    LeadDbContext context,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    ILogger<DsrService> logger) : IDsrService
{
    public async Task EraseAsync(Guid dsrId, Guid organizationId, IReadOnlyList<string> emails, IReadOnlyList<Guid> leadIds, CancellationToken ct)
    {
        var emailsLower = emails.Select(e => e.ToLowerInvariant()).ToArray();

        // Collect all leads to erase
        var toErase = new List<Domain.Entities.Lead>();

        if (emailsLower.Length > 0)
        {
            var byEmail = await context.Leads
                .Where(l => l.OrganizationId == organizationId && emailsLower.Contains(l.Email!))
                .ToListAsync(ct);
            toErase.AddRange(byEmail);
        }

        if (leadIds.Count > 0)
        {
            var byId = await context.Leads
                .Where(l => l.OrganizationId == organizationId && leadIds.Contains(l.Id))
                .ToListAsync(ct);

            foreach (var l in byId)
                if (!toErase.Any(x => x.Id == l.Id))
                    toErase.Add(l);
        }

        var erasedIds = new List<Guid>();

        foreach (var lead in toErase)
        {
            // Hard-delete web form submissions for this lead
            var submissions = await context.WebFormSubmissions
                .Where(s => s.LeadId == lead.Id)
                .ToListAsync(ct);
            context.WebFormSubmissions.RemoveRange(submissions);

            // Hard-delete the lead
            context.Leads.Remove(lead);
            erasedIds.Add(lead.Id);
        }

        // Publish dsr.erasure_completed
        outbox.Add(EventTypes.DsrErasureCompleted, AggregateTypes.Dsr, dsrId,
            organizationId, 1, null,
            new { dsr_id = dsrId, erased_count = erasedIds.Count, lead_ids = erasedIds });

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("DSR erasure {DsrId}: erased {Count} lead(s) in org {OrgId}.",
            dsrId, erasedIds.Count, organizationId);
    }
}
