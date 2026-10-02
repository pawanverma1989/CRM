namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Events;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Mapping;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;

/// <summary>
/// DEL-1..DEL-4. The retention window is enforced here and in
/// <see cref="VisibilityQueries.DeletedCompanies"/>, not by the nightly purge: the purge only
/// reclaims space, so the recycle bin stays truthful even if it has not run (AC-15).
/// </summary>
public interface IRecycleBinService
{
    /// <summary>DEL-4: soft-delete up to <c>Customer:MaxBulkRecords</c> records, one event each.</summary>
    Task<BulkActionResultDto> BulkDeleteAsync(BulkDeleteRequest request, CancellationToken ct);

    /// <summary>DEL-2: what an admin may still restore.</summary>
    Task<PagedResult<RecycleBinItemDto>> ListAsync(string? recordType, int page, int? pageSize, CancellationToken ct);

    /// <summary>DEL-2 / AC-13, AC-14.</summary>
    Task<object> RestoreAsync(string recordType, Guid id, CancellationToken ct);
}

public class RecycleBinService(
    ICompanyRepository companies,
    IContactRepository contacts,
    ILookupRepository lookups,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<CustomerSettings> settings) : IRecycleBinService
{
    private readonly CustomerSettings _settings = settings.Value;

    /// <summary>The oldest <c>deleted_at</c> that is still restorable (DEL-2, DEL-3).</summary>
    private DateTimeOffset RestorableSince
        => clock.GetUtcNow().AddDays(-_settings.RecycleBinRetentionDays);

    public async Task<BulkActionResultDto> BulkDeleteAsync(BulkDeleteRequest request, CancellationToken ct)
    {
        var recordType = EntityTypes.Require(request.RecordType);
        var ids = BulkGuard.Ids(request.RecordIds, _settings.MaxBulkRecords);

        var now = clock.GetUtcNow();
        var errors = new List<BulkItemErrorDto>();
        var succeeded = 0;

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

                try { PermissionGuard.EnsureCanEdit(ctx, company.OwnerId); }
                catch (ForbiddenException ex) { errors.Add(new BulkItemErrorDto(id, null, ex.Message)); continue; }

                // COM-5 applies to a bulk delete too: the contacts survive, unlinked.
                var linked = await contacts.LinkedToCompanyAsync(company.Id, ctx.OrganizationId, ct);
                foreach (var contact in linked)
                {
                    var before = RecordSnapshot.Of(contact);
                    contact.CompanyId = null;
                    contact.Version += 1;
                    contact.UpdatedAt = now;

                    outbox.Add(EventTypes.ContactUpdated, AggregateTypes.Contact, contact.Id,
                        contact.OrganizationId, contact.Version, ctx.ActorUserId,
                        new UpdatedEventPayload<ContactDto>(
                            CustomerMapper.ToDto(contact, LookupNames.Empty),
                            RecordSnapshot.Diff(before, RecordSnapshot.Of(contact))));
                }

                company.DeletedAt = now;
                company.Version += 1;
                company.UpdatedAt = now;

                outbox.Add(EventTypes.CompanyDeleted, AggregateTypes.Company, company.Id,
                    company.OrganizationId, company.Version, ctx.ActorUserId,
                    new DeletedEventPayload(company.Id, company.Version));

                succeeded++;
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

                try { PermissionGuard.EnsureCanEdit(ctx, contact.OwnerId); }
                catch (ForbiddenException ex) { errors.Add(new BulkItemErrorDto(id, null, ex.Message)); continue; }

                contact.DeletedAt = now;
                contact.Version += 1;
                contact.UpdatedAt = now;

                outbox.Add(EventTypes.ContactDeleted, AggregateTypes.Contact, contact.Id,
                    contact.OrganizationId, contact.Version, ctx.ActorUserId,
                    new DeletedEventPayload(contact.Id, contact.Version));

                succeeded++;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new BulkActionResultDto(succeeded, 0, errors);
    }

    public async Task<PagedResult<RecycleBinItemDto>> ListAsync(
        string? recordType, int page, int? pageSize, CancellationToken ct)
    {
        var since = RestorableSince;
        var resolvedPage = page < 1 ? 1 : page;
        var size = pageSize is null or < 1
            ? _settings.DefaultPageSize
            : Math.Min(pageSize.Value, _settings.MaxPageSize);

        var wanted = string.IsNullOrWhiteSpace(recordType)
            ? null
            : EntityTypes.Require(recordType);

        var items = new List<RecycleBinItemDto>();
        var total = 0;

        if (wanted is null or EntityTypes.Company)
        {
            total += await companies.CountDeletedAsync(ctx, since, ct);
            var deleted = await companies.ListDeletedAsync(ctx, since, resolvedPage, size, ct);
            var names = await lookups.NamesAsync(
                ctx.OrganizationId, deleted.Where(c => c.OwnerId is not null).Select(c => c.OwnerId!.Value), [], [], ct);

            items.AddRange(deleted.Select(c => new RecycleBinItemDto(
                c.Id, EntityTypes.Company, c.Name, null, c.Domain, c.OwnerId, names.Owner(c.OwnerId),
                c.DeletedAt!.Value, c.DeletedAt!.Value.AddDays(_settings.RecycleBinRetentionDays), c.Version)));
        }

        if (wanted is null or EntityTypes.Contact)
        {
            total += await contacts.CountDeletedAsync(ctx, since, ct);
            var deleted = await contacts.ListDeletedAsync(ctx, since, resolvedPage, size, ct);
            var names = await lookups.NamesAsync(
                ctx.OrganizationId, deleted.Where(c => c.OwnerId is not null).Select(c => c.OwnerId!.Value), [], [], ct);

            items.AddRange(deleted.Select(c => new RecycleBinItemDto(
                c.Id, EntityTypes.Contact, CustomerMapper.DisplayName(c), c.Email, null,
                c.OwnerId, names.Owner(c.OwnerId),
                c.DeletedAt!.Value, c.DeletedAt!.Value.AddDays(_settings.RecycleBinRetentionDays), c.Version)));
        }

        return new PagedResult<RecycleBinItemDto>(
            [.. items.OrderByDescending(i => i.DeletedAt)], resolvedPage, size, total);
    }

    public async Task<object> RestoreAsync(string recordType, Guid id, CancellationToken ct)
    {
        var type = EntityTypes.Require(recordType);
        var since = RestorableSince;
        var now = clock.GetUtcNow();

        if (type == EntityTypes.Company)
        {
            var company = await companies.GetDeletedAsync(id, ctx, since, ct)
                ?? throw new NotFoundException($"No restorable company with id {id}.");

            // DEL-2: restoring must not break DUP-1; the response names the blocking record.
            if (company.Domain is not null)
            {
                var clash = await companies.FindLiveByDomainAsync(ctx.OrganizationId, company.Domain, company.Id, ct);
                if (clash is not null)
                    throw ConflictException.Duplicate(
                        $"Another live company already uses the domain '{company.Domain}'.", clash.Id, clash.Name);
            }

            company.DeletedAt = null;
            company.Version += 1;
            company.UpdatedAt = now;

            var names = await lookups.NamesAsync(
                ctx.OrganizationId,
                company.OwnerId is null ? [] : [company.OwnerId.Value],
                company.IndustryId is null ? [] : [company.IndustryId.Value], [], ct);

            var dto = CustomerMapper.ToDto(company, names);
            outbox.Add(EventTypes.CompanyRestored, AggregateTypes.Company, company.Id,
                company.OrganizationId, company.Version, ctx.ActorUserId, dto);

            await unitOfWork.SaveChangesAsync(ct);
            return dto;
        }

        var contact = await contacts.GetDeletedAsync(id, ctx, since, ct)
            ?? throw new NotFoundException($"No restorable contact with id {id}.");

        // DEL-2 / AC-14.
        if (contact.Email is not null)
        {
            var clash = await contacts.FindLiveByEmailAsync(ctx.OrganizationId, contact.Email, contact.Id, ct);
            if (clash is not null)
                throw ConflictException.Duplicate(
                    "Another live contact already uses this email address.",
                    clash.Id, CustomerMapper.DisplayName(clash));
        }

        contact.DeletedAt = null;
        contact.Version += 1;
        contact.UpdatedAt = now;

        var contactNames = await lookups.NamesAsync(
            ctx.OrganizationId,
            contact.OwnerId is null ? [] : [contact.OwnerId.Value],
            contact.SourceId is null ? [] : [contact.SourceId.Value],
            contact.CompanyId is null ? [] : [contact.CompanyId.Value], ct);

        var contactDto = CustomerMapper.ToDto(contact, contactNames);
        outbox.Add(EventTypes.ContactRestored, AggregateTypes.Contact, contact.Id,
            contact.OrganizationId, contact.Version, ctx.ActorUserId, contactDto);

        await unitOfWork.SaveChangesAsync(ct);
        return contactDto;
    }
}
