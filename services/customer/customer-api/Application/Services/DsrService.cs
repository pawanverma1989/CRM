namespace CustomerApi.Application.Services;
using System.Text.Json;
using CustomerApi.Application.Events;
using CustomerApi.Application.Json;
using CustomerApi.Application.Mapping;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// DSR-1 and DSR-2 (§3.9, architecture §7.5). Driven by compliance events, never by a user
/// request. Nothing here logs a name, an email or a phone number — ids only (NFR-7).
/// </summary>
public interface IDsrService
{
    /// <summary>
    /// DSR-1 / AC-17: hard-deletes the named contacts including their deleted and merged copies,
    /// publishes <c>contact.purged</c> for each and replies <c>dsr.erasure_completed</c>.
    /// </summary>
    Task<int> EraseAsync(Guid dsrId, Guid organizationId, DsrSubjects subjects, CancellationToken ct);

    /// <summary>DSR-2: writes everything held about the named contacts as JSON and replies <c>dsr.access_completed</c>.</summary>
    Task<(int Records, string FileLocation)> ExportAsync(
        Guid dsrId, Guid organizationId, DsrSubjects subjects, CancellationToken ct);
}

/// <summary>The subjects a compliance request names: contact ids and/or email addresses.</summary>
public sealed record DsrSubjects(IReadOnlyList<Guid> ContactIds, IReadOnlyList<string> Emails)
{
    public static readonly DsrSubjects None = new([], []);
    public bool IsEmpty => ContactIds.Count == 0 && Emails.Count == 0;
}

public class DsrService(
    CustomerDbContext context,
    ILookupRepository lookups,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<CustomerSettings> settings,
    ILogger<DsrService> logger) : IDsrService
{
    private readonly CustomerSettings _settings = settings.Value;

    public async Task<int> EraseAsync(
        Guid dsrId, Guid organizationId, DsrSubjects subjects, CancellationToken ct)
    {
        var ids = await ResolveSubjectContactIdsAsync(organizationId, subjects, ct);

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        foreach (var id in ids)
            outbox.Add(EventTypes.ContactPurged, AggregateTypes.Contact, id,
                organizationId, 0, null, new PurgedEventPayload(id, "erasure"));

        outbox.Add(EventTypes.DsrErasureCompleted, AggregateTypes.Dsr, dsrId,
            organizationId, 0, null,
            new DsrErasureCompletedPayload(dsrId, "customer", ids.Count));

        await unitOfWork.SaveChangesAsync(ct);

        if (ids.Count > 0)
        {
            // merge_history names both sides of a merge, so it goes before the rows it points at.
            await unitOfWork.ExecuteSqlAsync(
                $"DELETE FROM merge_history WHERE organization_id = {organizationId} AND entity_type = 'contact' AND (survivor_id = ANY({ids}) OR loser_id = ANY({ids}))", ct);

            // Break the merged_into_id chain first; the column is a real foreign key inside this database.
            await unitOfWork.ExecuteSqlAsync(
                $"UPDATE contacts SET merged_into_id = NULL WHERE merged_into_id = ANY({ids})", ct);

            await unitOfWork.ExecuteSqlAsync(
                $"DELETE FROM contacts WHERE organization_id = {organizationId} AND id = ANY({ids})", ct);
        }

        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Erasure request {DsrId} removed {Count} contact record(s).", dsrId, ids.Count);

        return ids.Count;
    }

    public async Task<(int Records, string FileLocation)> ExportAsync(
        Guid dsrId, Guid organizationId, DsrSubjects subjects, CancellationToken ct)
    {
        var ids = await ResolveSubjectContactIdsAsync(organizationId, subjects, ct);

        var contacts = ids.Count == 0
            ? []
            : await context.Contacts
                .Where(c => c.OrganizationId == organizationId && ids.Contains(c.Id))
                .AsNoTracking()
                .ToListAsync(ct);

        var names = await lookups.NamesAsync(
            organizationId,
            contacts.Where(c => c.OwnerId is not null).Select(c => c.OwnerId!.Value),
            contacts.Where(c => c.SourceId is not null).Select(c => c.SourceId!.Value),
            contacts.Where(c => c.CompanyId is not null).Select(c => c.CompanyId!.Value),
            ct);

        var merges = ids.Count == 0
            ? []
            : await context.MergeHistories
                .Where(m => m.OrganizationId == organizationId
                         && m.EntityType == EntityTypes.Contact
                         && (ids.Contains(m.SurvivorId) || ids.Contains(m.LoserId)))
                .AsNoTracking()
                .ToListAsync(ct);

        Directory.CreateDirectory(_settings.DsrExportPath);
        var fileLocation = Path.Combine(_settings.DsrExportPath, $"dsr-{dsrId}-customer.json");

        var document = new
        {
            dsr_id = dsrId,
            service = "customer",
            generated_at = clock.GetUtcNow(),
            contacts = contacts.Select(c => CustomerMapper.ToDto(c, names)),
            merges = merges.Select(m => new { m.EntityType, m.SurvivorId, m.LoserId, m.MergedAt })
        };

        await File.WriteAllTextAsync(fileLocation, CustomerJson.SerializeEvent(document), ct);

        outbox.Add(EventTypes.DsrAccessCompleted, AggregateTypes.Dsr, dsrId,
            organizationId, 0, null,
            new DsrAccessCompletedPayload(dsrId, "customer", contacts.Count, fileLocation));

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Access request {DsrId} exported {Count} contact record(s).", dsrId, contacts.Count);

        return (contacts.Count, fileLocation);
    }

    /// <summary>
    /// AC-17: an erasure must reach the merged copies too, so the set is closed over
    /// <c>merged_into_id</c> and <c>merge_history</c> in both directions. Soft-deleted rows are
    /// included — the recycle bin is not a hiding place from an erasure request.
    /// </summary>
    private async Task<List<Guid>> ResolveSubjectContactIdsAsync(
        Guid organizationId, DsrSubjects subjects, CancellationToken ct)
    {
        if (subjects.IsEmpty) return [];

        var seedIds = subjects.ContactIds.Distinct().ToArray();
        var emails = subjects.Emails
            .Select(Validation.EmailValidator.Clean)
            .Where(e => e is not null)
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var found = new HashSet<Guid>(await context.Contacts
            .Where(c => c.OrganizationId == organizationId
                     && (seedIds.Contains(c.Id) || (c.Email != null && emails.Contains(c.Email))))
            .Select(c => c.Id)
            .ToListAsync(ct));

        for (var pass = 0; pass < 5; pass++)
        {
            var current = found.ToArray();

            var linked = await context.Contacts
                .Where(c => c.OrganizationId == organizationId
                         && c.MergedIntoId != null
                         && current.Contains(c.MergedIntoId.Value))
                .Select(c => c.Id)
                .ToListAsync(ct);

            var viaHistory = await context.MergeHistories
                .Where(m => m.OrganizationId == organizationId
                         && m.EntityType == EntityTypes.Contact
                         && (current.Contains(m.SurvivorId) || current.Contains(m.LoserId)))
                .Select(m => new { m.SurvivorId, m.LoserId })
                .ToListAsync(ct);

            var before = found.Count;
            foreach (var id in linked) found.Add(id);
            foreach (var pair in viaHistory) { found.Add(pair.SurvivorId); found.Add(pair.LoserId); }

            if (found.Count == before) break;
        }

        // merge_history may name a row that no longer exists; only real rows are reported.
        var ids = found.ToArray();
        return await context.Contacts
            .Where(c => c.OrganizationId == organizationId && ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
    }
}
