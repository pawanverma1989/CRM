namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Events;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Mapping;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>OWN-1..OWN-3: owners, bulk reassignment and the queue a deactivated user leaves behind.</summary>
public interface IOwnershipService
{
    /// <summary>OWN-2: change the owner of up to <c>Customer:MaxBulkRecords</c> records; one event per record.</summary>
    Task<BulkActionResultDto> ReassignAsync(ReassignRequest request, CancellationToken ct);

    /// <summary>OWN-3: the records of deactivated users, waiting for an admin.</summary>
    Task<IReadOnlyList<ReassignmentQueueItemDto>> QueueAsync(CancellationToken ct);

    /// <summary>The organization's owners, from the local <c>user_refs</c> copy.</summary>
    Task<IReadOnlyList<OwnerDto>> OwnersAsync(bool includeInactive, CancellationToken ct);
}

public class OwnershipService(
    ICompanyRepository companies,
    IContactRepository contacts,
    ILookupRepository lookups,
    IReassignmentRepository reassignments,
    CustomerDbContext context,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<CustomerSettings> settings) : IOwnershipService
{
    private readonly CustomerSettings _settings = settings.Value;

    public async Task<BulkActionResultDto> ReassignAsync(ReassignRequest request, CancellationToken ct)
    {
        var recordType = EntityTypes.Require(request.RecordType);
        var ids = BulkGuard.Ids(request.RecordIds, _settings.MaxBulkRecords);

        PermissionGuard.EnsureCanAssignOwner(ctx, request.NewOwnerId);
        await EnsureOwnerIsAssignableAsync(request.NewOwnerId, ct);

        var now = clock.GetUtcNow();
        var errors = new List<BulkItemErrorDto>();
        var succeeded = 0;
        var skipped = 0;
        var touched = new List<Guid>();

        if (recordType == EntityTypes.Company)
        {
            var found = await companies.GetManyAsync(ids, ctx, ct);
            foreach (var id in ids)
            {
                var company = found.FirstOrDefault(c => c.Id == id);
                if (company is null)
                {
                    errors.Add(new BulkItemErrorDto(id, null, "No such company, or it is not one you may edit."));
                    continue;
                }

                if (company.OwnerId == request.NewOwnerId) { skipped++; touched.Add(id); continue; }

                var before = RecordSnapshot.Of(company);
                var previousOwner = company.OwnerId;
                company.OwnerId = request.NewOwnerId;
                company.Version += 1;
                company.UpdatedAt = now;

                var names = await lookups.NamesAsync(
                    ctx.OrganizationId,
                    company.OwnerId is null ? [] : [company.OwnerId.Value],
                    company.IndustryId is null ? [] : [company.IndustryId.Value], [], ct);

                outbox.Add(EventTypes.CompanyUpdated, AggregateTypes.Company, company.Id,
                    company.OrganizationId, company.Version, ctx.ActorUserId,
                    new UpdatedEventPayload<CompanyDto>(
                        CustomerMapper.ToDto(company, names),
                        RecordSnapshot.Diff(before, RecordSnapshot.Of(company))));

                outbox.Add(EventTypes.CompanyReassigned, AggregateTypes.Company, company.Id,
                    company.OrganizationId, company.Version, ctx.ActorUserId,
                    new ReassignedEventPayload(company.Id, previousOwner, company.OwnerId));

                succeeded++;
                touched.Add(id);
            }
        }
        else
        {
            var found = await contacts.GetManyAsync(ids, ctx, ct);
            foreach (var id in ids)
            {
                var contact = found.FirstOrDefault(c => c.Id == id);
                if (contact is null)
                {
                    errors.Add(new BulkItemErrorDto(id, null, "No such contact, or it is not one you may edit."));
                    continue;
                }

                if (contact.OwnerId == request.NewOwnerId) { skipped++; touched.Add(id); continue; }

                var before = RecordSnapshot.Of(contact);
                var previousOwner = contact.OwnerId;
                contact.OwnerId = request.NewOwnerId;
                contact.Version += 1;
                contact.UpdatedAt = now;

                var names = await lookups.NamesAsync(
                    ctx.OrganizationId,
                    contact.OwnerId is null ? [] : [contact.OwnerId.Value],
                    contact.SourceId is null ? [] : [contact.SourceId.Value],
                    contact.CompanyId is null ? [] : [contact.CompanyId.Value], ct);

                outbox.Add(EventTypes.ContactUpdated, AggregateTypes.Contact, contact.Id,
                    contact.OrganizationId, contact.Version, ctx.ActorUserId,
                    new UpdatedEventPayload<ContactDto>(
                        CustomerMapper.ToDto(contact, names),
                        RecordSnapshot.Diff(before, RecordSnapshot.Of(contact))));

                outbox.Add(EventTypes.ContactReassigned, AggregateTypes.Contact, contact.Id,
                    contact.OrganizationId, contact.Version, ctx.ActorUserId,
                    new ReassignedEventPayload(contact.Id, previousOwner, contact.OwnerId));

                succeeded++;
                touched.Add(id);
            }
        }

        // OWN-3: a record that was waiting in the queue leaves it once it has a new owner.
        var open = await reassignments.OpenEntriesForRecordsAsync(ctx.OrganizationId, recordType, touched, ct);
        foreach (var entry in open)
        {
            entry.NewOwnerId = request.NewOwnerId;
            entry.ResolvedAt = now;
            entry.ResolvedBy = ctx.ActorUserId;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new BulkActionResultDto(succeeded, skipped, errors);
    }

    public async Task<IReadOnlyList<ReassignmentQueueItemDto>> QueueAsync(CancellationToken ct)
    {
        var entries = await reassignments.OpenEntriesAsync(ctx.OrganizationId, ct);
        if (entries.Count == 0) return [];

        var companyIds = entries.Where(e => e.RecordType == EntityTypes.Company).Select(e => e.RecordId).ToArray();
        var contactIds = entries.Where(e => e.RecordType == EntityTypes.Contact).Select(e => e.RecordId).ToArray();

        // OWN-3: queued records stay visible and editable, so the names come from live rows.
        var companyNames = companyIds.Length == 0
            ? []
            : await context.Companies
                .Where(c => c.OrganizationId == ctx.OrganizationId && companyIds.Contains(c.Id))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var contactNames = contactIds.Length == 0
            ? []
            : await context.Contacts
                .Where(c => c.OrganizationId == ctx.OrganizationId && contactIds.Contains(c.Id))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => CustomerMapper.DisplayName(c), ct);

        var ownerNames = await lookups.NamesAsync(
            ctx.OrganizationId, entries.Select(e => e.PreviousOwnerId), [], [], ct);

        return
        [
            .. entries.Select(e => new ReassignmentQueueItemDto(
                e.Id,
                e.RecordType,
                e.RecordId,
                e.RecordType == EntityTypes.Company
                    ? companyNames.GetValueOrDefault(e.RecordId, string.Empty)
                    : contactNames.GetValueOrDefault(e.RecordId, string.Empty),
                e.PreviousOwnerId,
                ownerNames.Owner(e.PreviousOwnerId),
                e.QueuedAt))
        ];
    }

    public async Task<IReadOnlyList<OwnerDto>> OwnersAsync(bool includeInactive, CancellationToken ct)
    {
        var owners = await lookups.OwnersAsync(ctx.OrganizationId, activeOnly: !includeInactive, ct);
        return [.. owners.Select(CustomerMapper.ToDto)];
    }

    /// <summary>§4: an owner must be an active user the editor may assign.</summary>
    private async Task EnsureOwnerIsAssignableAsync(Guid? ownerId, CancellationToken ct)
    {
        if (ownerId is null) return;

        var userRef = await reassignments.UserRefAsync(ownerId.Value, ct);

        // user_refs is a local copy fed by identity events. An unknown id is accepted — the copy
        // may simply be behind — but a known, deactivated user is refused.
        if (userRef is not null && (!userRef.IsActive || userRef.OrganizationId != ctx.OrganizationId))
            throw new CustomerValidationException("newOwnerId", "That user cannot own records.");
    }
}

/// <summary>DEL-4 / OWN-2: the shared guard on bulk record lists.</summary>
public static class BulkGuard
{
    public static Guid[] Ids(IReadOnlyList<Guid>? ids, int max)
    {
        var distinct = (ids ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();

        if (distinct.Length == 0)
            throw new CustomerValidationException("recordIds", "Name at least one record.");

        if (distinct.Length > max)
            throw new CustomerValidationException("recordIds", $"At most {max} records per call.");

        return distinct;
    }
}
