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

/// <summary>Company CRUD: COM-1..COM-5, DUP-1, LST-1..LST-4, DEL-1, OWN-1, OWN-2.</summary>
public interface ICompanyService
{
    Task<PagedResult<CompanyDto>> ListAsync(CompanyListQuery query, CancellationToken ct, int? maxPageSize = null);
    Task<CompanyDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<CompanyDto> CreateAsync(CreateCompanyRequest request, CancellationToken ct);
    Task<CompanyDto> UpdateAsync(Guid id, UpdateCompanyRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public class CompanyService(
    ICompanyRepository companies,
    IContactRepository contacts,
    ILookupRepository lookups,
    IReferenceDataLoader reference,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<CustomerSettings> settings) : ICompanyService
{
    private readonly CustomerSettings _settings = settings.Value;

    public async Task<PagedResult<CompanyDto>> ListAsync(
        CompanyListQuery query, CancellationToken ct, int? maxPageSize = null)
    {
        var (page, pageSize) = ListPaging.Resolve(query, _settings, maxPageSize);
        var (items, total) = await companies.ListAsync(query, ctx, ct, maxPageSize);
        var names = await NamesForAsync(items, ct);

        return new PagedResult<CompanyDto>(
            [.. items.Select(c => CustomerMapper.ToDto(c, names))], page, pageSize, total);
    }

    public async Task<CompanyDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var company = await companies.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        var companyContacts = await contacts.ForCompanyAsync(id, ctx, ct);

        // COM-2: the detail view carries the company's contacts; deals and the timeline are
        // loaded by the client from the Sales and Activity services (CLAUDE.md rule 1).
        var names = await lookups.NamesAsync(
            ctx.OrganizationId,
            companyContacts.Select(c => c.OwnerId).Append(company.OwnerId).Where(o => o is not null).Select(o => o!.Value),
            company.IndustryId is null ? [] : [company.IndustryId.Value],
            [],
            ct);

        return new CompanyDetailDto(
            CustomerMapper.ToDto(company, names),
            [.. companyContacts.Select(c => CustomerMapper.ToSummary(c, names))]);
    }

    public async Task<CompanyDto> CreateAsync(CreateCompanyRequest request, CancellationToken ct)
    {
        var writeReference = await reference.ForAsync(EntityTypes.Company, ct);
        var errors = new Collector();

        var now = clock.GetUtcNow();
        var company = new Company
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            OwnerId = PermissionGuard.ResolveOwnerOnCreate(ctx, request.OwnerId),
            CustomFields = "{}",
            Version = 1,
            CreatedBy = ctx.ActorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        RecordBinders.ApplyCreate(company, request, writeReference, errors);
        errors.ThrowIfInvalid();

        await EnsureDomainIsFreeAsync(company.Domain, null, ct);

        companies.Add(company);

        var dto = await ToDtoAsync(company, ct);
        outbox.Add(EventTypes.CompanyCreated, AggregateTypes.Company, company.Id,
            company.OrganizationId, company.Version, ctx.ActorUserId, dto);

        await unitOfWork.SaveChangesAsync(ct);
        return dto;
    }

    public async Task<CompanyDto> UpdateAsync(Guid id, UpdateCompanyRequest request, CancellationToken ct)
    {
        var company = await companies.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, company.OwnerId);

        // COM-3 / AC-6: a save based on an older version is a conflict, not a silent overwrite.
        if (request.Version != company.Version)
            throw new ConflictException(
                $"This company has changed since you loaded it (version {company.Version}, you sent {request.Version}).");

        var before = RecordSnapshot.Of(company);
        var previousOwner = company.OwnerId;

        if (request.OwnerId.HasValue && request.OwnerId.Value != company.OwnerId)
        {
            PermissionGuard.EnsureCanAssignOwner(ctx, request.OwnerId.Value);
            company.OwnerId = request.OwnerId.Value;
        }

        var writeReference = await reference.ForAsync(EntityTypes.Company, ct);
        var errors = new Collector();
        RecordBinders.ApplyPatch(company, request, writeReference, errors);
        errors.ThrowIfInvalid();

        var changes = RecordSnapshot.Diff(before, RecordSnapshot.Of(company));
        if (changes.Count == 0) return await ToDtoAsync(company, ct);

        await EnsureDomainIsFreeAsync(company.Domain, company.Id, ct);

        company.Version = request.Version + 1;
        company.UpdatedAt = clock.GetUtcNow();

        var dto = await ToDtoAsync(company, ct);
        outbox.Add(EventTypes.CompanyUpdated, AggregateTypes.Company, company.Id,
            company.OrganizationId, company.Version, ctx.ActorUserId,
            new UpdatedEventPayload<CompanyDto>(dto, changes));

        // OWN-2: an owner change notifies the new owner through its own event.
        if (previousOwner != company.OwnerId)
            outbox.Add(EventTypes.CompanyReassigned, AggregateTypes.Company, company.Id,
                company.OrganizationId, company.Version, ctx.ActorUserId,
                new ReassignedEventPayload(company.Id, previousOwner, company.OwnerId));

        await unitOfWork.SaveChangesAsync(ct);
        return dto;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var company = await companies.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, company.OwnerId);

        var now = clock.GetUtcNow();

        // COM-5: the contacts survive, unlinked. Every one of them changes, so every one of them
        // gets a contact.updated event — consumers keep their local copies right.
        var linked = await contacts.LinkedToCompanyAsync(id, ctx.OrganizationId, ct);
        foreach (var contact in linked)
        {
            var before = RecordSnapshot.Of(contact);
            contact.CompanyId = null;
            contact.Version += 1;
            contact.UpdatedAt = now;

            var contactDto = CustomerMapper.ToDto(contact, LookupNames.Empty);
            outbox.Add(EventTypes.ContactUpdated, AggregateTypes.Contact, contact.Id,
                contact.OrganizationId, contact.Version, ctx.ActorUserId,
                new UpdatedEventPayload<ContactDto>(
                    contactDto, RecordSnapshot.Diff(before, RecordSnapshot.Of(contact))));
        }

        company.DeletedAt = now;
        company.Version += 1;
        company.UpdatedAt = now;

        outbox.Add(EventTypes.CompanyDeleted, AggregateTypes.Company, company.Id,
            company.OrganizationId, company.Version, ctx.ActorUserId,
            new DeletedEventPayload(company.Id, company.Version));

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>DUP-1 / AC-4: organization-wide, so the save is blocked and the record named even when the caller cannot see it.</summary>
    private async Task EnsureDomainIsFreeAsync(string? domain, Guid? excludeId, CancellationToken ct)
    {
        if (domain is null) return;

        var existing = await companies.FindLiveByDomainAsync(ctx.OrganizationId, domain, excludeId, ct);
        if (existing is null) return;

        throw ConflictException.Duplicate(
            $"Another company already uses the domain '{domain}'.", existing.Id, existing.Name);
    }

    private async Task<CompanyDto> ToDtoAsync(Company company, CancellationToken ct)
    {
        var names = await lookups.NamesAsync(
            ctx.OrganizationId,
            company.OwnerId is null ? [] : [company.OwnerId.Value],
            company.IndustryId is null ? [] : [company.IndustryId.Value],
            [],
            ct);

        return CustomerMapper.ToDto(company, names);
    }

    private Task<LookupNames> NamesForAsync(IReadOnlyCollection<Company> items, CancellationToken ct)
        => lookups.NamesAsync(
            ctx.OrganizationId,
            items.Where(c => c.OwnerId is not null).Select(c => c.OwnerId!.Value),
            items.Where(c => c.IndustryId is not null).Select(c => c.IndustryId!.Value),
            [],
            ct);

    private static NotFoundException NotFound(Guid id) => new($"No company with id {id}.");
}
