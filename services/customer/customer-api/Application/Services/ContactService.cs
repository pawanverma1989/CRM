namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Events;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Mapping;
using CustomerApi.Application.Queries;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;
using Collector = CustomerApi.Application.Validation.StandardFieldValidator.Collector;

/// <summary>Contact CRUD: CON-1..CON-7, DUP-1, LST-1..LST-4, DEL-1, OWN-1, OWN-2.</summary>
public interface IContactService
{
    Task<PagedResult<ContactDto>> ListAsync(ContactListQuery query, CancellationToken ct, int? maxPageSize = null);
    Task<ContactDto> GetAsync(Guid id, CancellationToken ct);

    /// <summary><c>Created</c> is false when CON-7 returned the contact an earlier lead conversion made (AC-16).</summary>
    Task<(ContactDto Contact, bool Created)> CreateAsync(CreateContactRequest request, CancellationToken ct);

    Task<ContactDto> UpdateAsync(Guid id, UpdateContactRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public class ContactService(
    IContactRepository contacts,
    ICompanyRepository companies,
    ILookupRepository lookups,
    IReferenceDataLoader reference,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<CustomerSettings> settings) : IContactService
{
    private readonly CustomerSettings _settings = settings.Value;

    public async Task<PagedResult<ContactDto>> ListAsync(
        ContactListQuery query, CancellationToken ct, int? maxPageSize = null)
    {
        var (page, pageSize) = ListPaging.Resolve(query, _settings, maxPageSize);
        var (items, total) = await contacts.ListAsync(query, ctx, ct, maxPageSize);
        var names = await NamesForAsync(items, ct);

        return new PagedResult<ContactDto>(
            [.. items.Select(c => CustomerMapper.ToDto(c, names))], page, pageSize, total);
    }

    public async Task<ContactDto> GetAsync(Guid id, CancellationToken ct)
    {
        var contact = await contacts.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        return await ToDtoAsync(contact, ct);
    }

    public async Task<(ContactDto Contact, bool Created)> CreateAsync(
        CreateContactRequest request, CancellationToken ct)
    {
        // CON-7 / AC-16: a retried lead conversion returns the contact it already made. Checked
        // before anything else so the retry never trips DUP-1 on its own email.
        if (request.SourceLeadId is { } leadId)
        {
            var existing = await contacts.FindBySourceLeadAsync(leadId, ct);
            if (existing is not null)
                return (await ToDtoAsync(existing, ct), false);
        }

        var writeReference = await reference.ForAsync(EntityTypes.Contact, ct);
        var errors = new Collector();

        var now = clock.GetUtcNow();
        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            OwnerId = PermissionGuard.ResolveOwnerOnCreate(ctx, request.OwnerId),
            SourceLeadId = request.SourceLeadId,
            CustomFields = "{}",
            Version = 1,
            CreatedBy = ctx.ActorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        RecordBinders.ApplyCreate(contact, request, writeReference, errors);
        contact.CompanyId = await ResolveCompanyAsync(request.CompanyId, errors, ct);
        errors.ThrowIfInvalid();

        await EnsureEmailIsFreeAsync(contact.Email, null, ct);

        contacts.Add(contact);

        var dto = await ToDtoAsync(contact, ct);
        outbox.Add(EventTypes.ContactCreated, AggregateTypes.Contact, contact.Id,
            contact.OrganizationId, contact.Version, ctx.ActorUserId, dto);

        await unitOfWork.SaveChangesAsync(ct);
        return (dto, true);
    }

    public async Task<ContactDto> UpdateAsync(Guid id, UpdateContactRequest request, CancellationToken ct)
    {
        var contact = await contacts.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, contact.OwnerId);

        // CON-6 / AC-6.
        if (request.Version != contact.Version)
            throw new ConflictException(
                $"This contact has changed since you loaded it (version {contact.Version}, you sent {request.Version}).");

        var before = RecordSnapshot.Of(contact);
        var previousOwner = contact.OwnerId;

        if (request.OwnerId.HasValue && request.OwnerId.Value != contact.OwnerId)
        {
            PermissionGuard.EnsureCanAssignOwner(ctx, request.OwnerId.Value);
            contact.OwnerId = request.OwnerId.Value;
        }

        var writeReference = await reference.ForAsync(EntityTypes.Contact, ct);
        var errors = new Collector();
        RecordBinders.ApplyPatch(contact, request, writeReference, errors);

        // CON-2: the company link can be changed at any time, including to none.
        if (request.CompanyId.HasValue)
            contact.CompanyId = await ResolveCompanyAsync(request.CompanyId.Value, errors, ct);

        errors.ThrowIfInvalid();

        var changes = RecordSnapshot.Diff(before, RecordSnapshot.Of(contact));
        if (changes.Count == 0) return await ToDtoAsync(contact, ct);

        await EnsureEmailIsFreeAsync(contact.Email, contact.Id, ct);

        contact.Version = request.Version + 1;
        contact.UpdatedAt = clock.GetUtcNow();

        var dto = await ToDtoAsync(contact, ct);
        outbox.Add(EventTypes.ContactUpdated, AggregateTypes.Contact, contact.Id,
            contact.OrganizationId, contact.Version, ctx.ActorUserId,
            new UpdatedEventPayload<ContactDto>(dto, changes));

        if (previousOwner != contact.OwnerId)
            outbox.Add(EventTypes.ContactReassigned, AggregateTypes.Contact, contact.Id,
                contact.OrganizationId, contact.Version, ctx.ActorUserId,
                new ReassignedEventPayload(contact.Id, previousOwner, contact.OwnerId));

        await unitOfWork.SaveChangesAsync(ct);
        return dto;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var contact = await contacts.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, contact.OwnerId);

        var now = clock.GetUtcNow();
        contact.DeletedAt = now;
        contact.Version += 1;
        contact.UpdatedAt = now;

        outbox.Add(EventTypes.ContactDeleted, AggregateTypes.Contact, contact.Id,
            contact.OrganizationId, contact.Version, ctx.ActorUserId,
            new DeletedEventPayload(contact.Id, contact.Version));

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>§4.2: a contact may link to one live company the editor may view.</summary>
    private async Task<Guid?> ResolveCompanyAsync(Guid? companyId, Collector errors, CancellationToken ct)
    {
        if (companyId is null) return null;

        var company = await companies.GetAsync(companyId.Value, ctx, ct);
        if (company is not null) return company.Id;

        errors.Add("companyId", "No such company, or it is not one you may use.");
        return null;
    }

    /// <summary>DUP-1 / AC-3: emails are compared case-insensitively and the blocking record is named.</summary>
    private async Task EnsureEmailIsFreeAsync(string? email, Guid? excludeId, CancellationToken ct)
    {
        if (email is null) return;

        var existing = await contacts.FindLiveByEmailAsync(ctx.OrganizationId, email, excludeId, ct);
        if (existing is null) return;

        throw ConflictException.Duplicate(
            "Another contact already uses this email address.", existing.Id, CustomerMapper.DisplayName(existing));
    }

    private async Task<ContactDto> ToDtoAsync(Contact contact, CancellationToken ct)
    {
        var names = await lookups.NamesAsync(
            ctx.OrganizationId,
            contact.OwnerId is null ? [] : [contact.OwnerId.Value],
            contact.SourceId is null ? [] : [contact.SourceId.Value],
            contact.CompanyId is null ? [] : [contact.CompanyId.Value],
            ct);

        return CustomerMapper.ToDto(contact, names);
    }

    private Task<LookupNames> NamesForAsync(IReadOnlyCollection<Contact> items, CancellationToken ct)
        => lookups.NamesAsync(
            ctx.OrganizationId,
            items.Where(c => c.OwnerId is not null).Select(c => c.OwnerId!.Value),
            items.Where(c => c.SourceId is not null).Select(c => c.SourceId!.Value),
            items.Where(c => c.CompanyId is not null).Select(c => c.CompanyId!.Value),
            ct);

    private static NotFoundException NotFound(Guid id) => new($"No contact with id {id}.");
}
