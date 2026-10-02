namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Events;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Mapping;
using CustomerApi.Application.Normalization;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Collector = CustomerApi.Application.Validation.StandardFieldValidator.Collector;

/// <summary>
/// <c>POST /bulk-upsert</c>, used by the data transfer service for CSV import (§5, AC-18).
/// Rows are validated exactly as hand-typed records are (CF-3, DUP-1), one result is returned per
/// row, and the duplicate strategy decides whether a matching live record is skipped, updated or
/// reported. Reference data and the matching live records are read once for the whole batch, so
/// 1,000 rows stay inside NFR-3's 10 seconds.
/// </summary>
public interface IBulkImportService
{
    Task<BulkUpsertResponse> UpsertAsync(BulkUpsertRequest request, CancellationToken ct);
}

public class BulkImportService(
    CustomerDbContext context,
    IReferenceDataLoader reference,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<CustomerSettings> settings) : IBulkImportService
{
    private const string Created = "created";
    private const string Updated = "updated";
    private const string Skipped = "skipped";
    private const string Failed = "error";

    private static readonly string[] Strategies = ["skip", "update", "create"];

    private readonly CustomerSettings _settings = settings.Value;

    public async Task<BulkUpsertResponse> UpsertAsync(BulkUpsertRequest request, CancellationToken ct)
    {
        var entityType = EntityTypes.Require(request.EntityType);
        var strategy = (request.DuplicateStrategy ?? "skip").Trim().ToLowerInvariant();

        if (!Strategies.Contains(strategy))
            throw new CustomerValidationException("duplicateStrategy",
                $"Must be one of: {string.Join(", ", Strategies)}.");

        if (request.Rows.Count == 0)
            throw new CustomerValidationException("rows", "Send at least one row.");

        if (request.Rows.Count > _settings.MaxImportRowsPerCall)
            throw new CustomerValidationException("rows",
                $"At most {_settings.MaxImportRowsPerCall} rows per call.");

        var writeReference = await reference.ForAsync(entityType, ct);

        var results = entityType == EntityTypes.Company
            ? await UpsertCompaniesAsync(request, strategy, writeReference, ct)
            : await UpsertContactsAsync(request, strategy, writeReference, ct);

        return new BulkUpsertResponse(
            results.Count(r => r.Status == Created),
            results.Count(r => r.Status == Updated),
            results.Count(r => r.Status == Skipped),
            results.Count(r => r.Status == Failed),
            results);
    }

    private async Task<List<BulkUpsertRowResultDto>> UpsertContactsAsync(
        BulkUpsertRequest request, string strategy, WriteReference writeReference, CancellationToken ct)
    {
        var results = new List<BulkUpsertRowResultDto>(request.Rows.Count);
        var now = clock.GetUtcNow();

        // One lookup for every email in the batch rather than one per row (NFR-3).
        var emails = request.Rows
            .Select(r => Validation.EmailValidator.Clean(r.Email))
            .Where(e => e is not null)
            .Select(e => e!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existing = emails.Length == 0
            ? []
            : await context.Contacts
                .Where(c => c.OrganizationId == ctx.OrganizationId
                         && c.DeletedAt == null
                         && c.MergedIntoId == null
                         && c.Email != null
                         && emails.Contains(c.Email))
                .ToListAsync(ct);

        var byEmail = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in existing) byEmail.TryAdd(contact.Email!, contact);

        var knownCompanies = await KnownCompanyIdsAsync(request.Rows, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        for (var index = 0; index < request.Rows.Count; index++)
        {
            var row = request.Rows[index];
            var email = Validation.EmailValidator.Clean(row.Email);

            if (email is not null && !seen.Add(email))
            {
                results.Add(new BulkUpsertRowResultDto(index, Failed, null,
                    "This email appears more than once in the import."));
                continue;
            }

            var match = email is not null && byEmail.TryGetValue(email, out var found) ? found : null;

            if (match is not null && strategy == "skip")
            {
                results.Add(new BulkUpsertRowResultDto(index, Skipped, match.Id, null));
                continue;
            }

            if (match is not null && strategy == "create")
            {
                results.Add(new BulkUpsertRowResultDto(index, Failed, match.Id,
                    "Another live contact already uses this email address."));
                continue;
            }

            var errors = new Collector();

            if (match is not null)
            {
                var before = RecordSnapshot.Of(match);
                var patch = ToUpdateRequest(row, match.Version);

                RecordBinders.ApplyPatch(match, patch, writeReference, errors);
                ApplyCompanyLink(match, row, knownCompanies, errors);
                ApplyOwner(row, errors, isNew: false, current: match.OwnerId, assign: owner => match.OwnerId = owner);

                if (errors.HasErrors)
                {
                    Revert(match);
                    results.Add(new BulkUpsertRowResultDto(index, Failed, match.Id, errors.Summary()));
                    continue;
                }

                var changes = RecordSnapshot.Diff(before, RecordSnapshot.Of(match));
                if (changes.Count == 0)
                {
                    results.Add(new BulkUpsertRowResultDto(index, Skipped, match.Id, null));
                    continue;
                }

                match.Version += 1;
                match.UpdatedAt = now;

                outbox.Add(EventTypes.ContactUpdated, AggregateTypes.Contact, match.Id,
                    match.OrganizationId, match.Version, ctx.ActorUserId,
                    new UpdatedEventPayload<ContactDto>(
                        CustomerMapper.ToDto(match, LookupNames.Empty), changes));

                results.Add(new BulkUpsertRowResultDto(index, Updated, match.Id, null));
                continue;
            }

            var contact = new Contact
            {
                Id = Guid.NewGuid(),
                OrganizationId = ctx.OrganizationId,
                CustomFields = "{}",
                Version = 1,
                CreatedBy = ctx.ActorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

            RecordBinders.ApplyCreate(contact, ToCreateContactRequest(row), writeReference, errors);
            ApplyCompanyLink(contact, row, knownCompanies, errors);
            ApplyOwner(row, errors, isNew: true, current: null, assign: owner => contact.OwnerId = owner);

            if (errors.HasErrors)
            {
                results.Add(new BulkUpsertRowResultDto(index, Failed, null, errors.Summary()));
                continue;
            }

            context.Contacts.Add(contact);
            if (email is not null) byEmail[email] = contact;

            outbox.Add(EventTypes.ContactCreated, AggregateTypes.Contact, contact.Id,
                contact.OrganizationId, contact.Version, ctx.ActorUserId,
                CustomerMapper.ToDto(contact, LookupNames.Empty));

            results.Add(new BulkUpsertRowResultDto(index, Created, contact.Id, null));
        }

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return results;
    }

    private async Task<List<BulkUpsertRowResultDto>> UpsertCompaniesAsync(
        BulkUpsertRequest request, string strategy, WriteReference writeReference, CancellationToken ct)
    {
        var results = new List<BulkUpsertRowResultDto>(request.Rows.Count);
        var now = clock.GetUtcNow();

        var domains = request.Rows
            .Select(r => DomainNormalizer.Normalize(r.Domain))
            .Where(d => d is not null)
            .Select(d => d!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existing = domains.Length == 0
            ? []
            : await context.Companies
                .Where(c => c.OrganizationId == ctx.OrganizationId
                         && c.DeletedAt == null
                         && c.MergedIntoId == null
                         && c.Domain != null
                         && domains.Contains(c.Domain))
                .ToListAsync(ct);

        var byDomain = new Dictionary<string, Company>(StringComparer.OrdinalIgnoreCase);
        foreach (var company in existing) byDomain.TryAdd(company.Domain!, company);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        for (var index = 0; index < request.Rows.Count; index++)
        {
            var row = request.Rows[index];
            var domain = DomainNormalizer.Normalize(row.Domain);

            if (domain is not null && !seen.Add(domain))
            {
                results.Add(new BulkUpsertRowResultDto(index, Failed, null,
                    "This domain appears more than once in the import."));
                continue;
            }

            var match = domain is not null && byDomain.TryGetValue(domain, out var found) ? found : null;

            if (match is not null && strategy == "skip")
            {
                results.Add(new BulkUpsertRowResultDto(index, Skipped, match.Id, null));
                continue;
            }

            if (match is not null && strategy == "create")
            {
                results.Add(new BulkUpsertRowResultDto(index, Failed, match.Id,
                    "Another live company already uses this domain."));
                continue;
            }

            var errors = new Collector();

            if (match is not null)
            {
                var before = RecordSnapshot.Of(match);
                RecordBinders.ApplyPatch(match, ToUpdateCompanyRequest(row, match.Version), writeReference, errors);
                ApplyOwner(row, errors, isNew: false, current: match.OwnerId, assign: owner => match.OwnerId = owner);

                if (errors.HasErrors)
                {
                    Revert(match);
                    results.Add(new BulkUpsertRowResultDto(index, Failed, match.Id, errors.Summary()));
                    continue;
                }

                var changes = RecordSnapshot.Diff(before, RecordSnapshot.Of(match));
                if (changes.Count == 0)
                {
                    results.Add(new BulkUpsertRowResultDto(index, Skipped, match.Id, null));
                    continue;
                }

                match.Version += 1;
                match.UpdatedAt = now;

                outbox.Add(EventTypes.CompanyUpdated, AggregateTypes.Company, match.Id,
                    match.OrganizationId, match.Version, ctx.ActorUserId,
                    new UpdatedEventPayload<CompanyDto>(
                        CustomerMapper.ToDto(match, LookupNames.Empty), changes));

                results.Add(new BulkUpsertRowResultDto(index, Updated, match.Id, null));
                continue;
            }

            var created = new Company
            {
                Id = Guid.NewGuid(),
                OrganizationId = ctx.OrganizationId,
                CustomFields = "{}",
                Version = 1,
                CreatedBy = ctx.ActorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

            RecordBinders.ApplyCreate(created, ToCreateCompanyRequest(row), writeReference, errors);
            ApplyOwner(row, errors, isNew: true, current: null, assign: owner => created.OwnerId = owner);

            if (errors.HasErrors)
            {
                results.Add(new BulkUpsertRowResultDto(index, Failed, null, errors.Summary()));
                continue;
            }

            context.Companies.Add(created);
            if (domain is not null) byDomain[domain] = created;

            outbox.Add(EventTypes.CompanyCreated, AggregateTypes.Company, created.Id,
                created.OrganizationId, created.Version, ctx.ActorUserId,
                CustomerMapper.ToDto(created, LookupNames.Empty));

            results.Add(new BulkUpsertRowResultDto(index, Created, created.Id, null));
        }

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return results;
    }

    /// <summary>Company ids the batch refers to that really exist in this organization.</summary>
    private async Task<HashSet<Guid>> KnownCompanyIdsAsync(
        IReadOnlyList<BulkUpsertRow> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.CompanyId is not null).Select(r => r.CompanyId!.Value).Distinct().ToArray();
        if (ids.Length == 0) return [];

        var found = await context.Companies
            .Where(c => c.OrganizationId == ctx.OrganizationId && c.DeletedAt == null && ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);

        return [.. found];
    }

    private static void ApplyCompanyLink(
        Contact contact, BulkUpsertRow row, HashSet<Guid> knownCompanies, Collector errors)
    {
        if (row.CompanyId is null) return;

        if (!knownCompanies.Contains(row.CompanyId.Value))
        {
            errors.Add("companyId", "No such company in this organization.");
            return;
        }

        contact.CompanyId = row.CompanyId;
    }

    /// <summary>
    /// §2: the data transfer service imports on behalf of a user, so an imported row is owned by
    /// that user unless they may name someone else.
    /// </summary>
    private void ApplyOwner(BulkUpsertRow row, Collector errors, bool isNew, Guid? current, Action<Guid?> assign)
    {
        if (row.OwnerId is null)
        {
            // A row that names no owner leaves an existing record's owner alone and gives a new
            // record the importing user as its owner (OWN-1).
            if (isNew) assign(ctx.ActorUserId);
            return;
        }

        if (row.OwnerId == current) return;

        if (!ctx.IsManagerOrAbove && row.OwnerId != ctx.ActorUserId)
        {
            errors.Add("ownerId", "You may not assign this owner.");
            return;
        }

        if (!ctx.CanSeeOwner(row.OwnerId))
        {
            errors.Add("ownerId", "You may not assign this owner.");
            return;
        }

        assign(row.OwnerId);
    }

    /// <summary>
    /// Rolls a row's failed edits back out of the change tracker, so one bad row never reaches the
    /// database and the in-memory copy stays the one the batch read.
    /// </summary>
    private void Revert<T>(T entity) where T : class
    {
        var entry = context.Entry(entity);
        entry.CurrentValues.SetValues(entry.OriginalValues);
        entry.State = EntityState.Unchanged;
    }

    private static CreateContactRequest ToCreateContactRequest(BulkUpsertRow row) => new()
    {
        FirstName = row.FirstName ?? string.Empty,
        LastName = row.LastName,
        Email = row.Email,
        Phone = row.Phone,
        Mobile = row.Mobile,
        JobTitle = row.JobTitle,
        SourceId = row.SourceId,
        AddressLine1 = row.AddressLine1,
        AddressLine2 = row.AddressLine2,
        City = row.City,
        State = row.State,
        PostalCode = row.PostalCode,
        Country = row.Country,
        Tags = row.Tags,
        CustomFields = row.CustomFields
    };

    private static CreateCompanyRequest ToCreateCompanyRequest(BulkUpsertRow row) => new()
    {
        Name = row.Name ?? string.Empty,
        Domain = row.Domain,
        IndustryId = row.IndustryId,
        EmployeeCount = row.EmployeeCount,
        AnnualRevenue = row.AnnualRevenue,
        Phone = row.Phone,
        Website = row.Website,
        AddressLine1 = row.AddressLine1,
        AddressLine2 = row.AddressLine2,
        City = row.City,
        State = row.State,
        PostalCode = row.PostalCode,
        Country = row.Country,
        Gstin = row.Gstin,
        Tags = row.Tags,
        CustomFields = row.CustomFields
    };

    /// <summary>An import leaves a column it does not carry alone, so only supplied values become a patch.</summary>
    private static UpdateContactRequest ToUpdateRequest(BulkUpsertRow row, int version)
    {
        var patch = new UpdateContactRequest { Version = version };

        if (row.FirstName is not null) patch = patch with { FirstName = row.FirstName };
        if (row.LastName is not null) patch = patch with { LastName = row.LastName };
        if (row.Phone is not null) patch = patch with { Phone = row.Phone };
        if (row.Mobile is not null) patch = patch with { Mobile = row.Mobile };
        if (row.JobTitle is not null) patch = patch with { JobTitle = row.JobTitle };
        if (row.SourceId is not null) patch = patch with { SourceId = row.SourceId };
        if (row.AddressLine1 is not null) patch = patch with { AddressLine1 = row.AddressLine1 };
        if (row.AddressLine2 is not null) patch = patch with { AddressLine2 = row.AddressLine2 };
        if (row.City is not null) patch = patch with { City = row.City };
        if (row.State is not null) patch = patch with { State = row.State };
        if (row.PostalCode is not null) patch = patch with { PostalCode = row.PostalCode };
        if (row.Country is not null) patch = patch with { Country = row.Country };
        if (row.Tags is not null) patch = patch with { Tags = new(row.Tags) };
        if (row.CustomFields is not null) patch = patch with { CustomFields = new(row.CustomFields) };

        return patch;
    }

    private static UpdateCompanyRequest ToUpdateCompanyRequest(BulkUpsertRow row, int version)
    {
        var patch = new UpdateCompanyRequest { Version = version };

        if (row.Name is not null) patch = patch with { Name = row.Name };
        if (row.IndustryId is not null) patch = patch with { IndustryId = row.IndustryId };
        if (row.EmployeeCount is not null) patch = patch with { EmployeeCount = row.EmployeeCount };
        if (row.AnnualRevenue is not null) patch = patch with { AnnualRevenue = row.AnnualRevenue };
        if (row.Phone is not null) patch = patch with { Phone = row.Phone };
        if (row.Website is not null) patch = patch with { Website = row.Website };
        if (row.AddressLine1 is not null) patch = patch with { AddressLine1 = row.AddressLine1 };
        if (row.AddressLine2 is not null) patch = patch with { AddressLine2 = row.AddressLine2 };
        if (row.City is not null) patch = patch with { City = row.City };
        if (row.State is not null) patch = patch with { State = row.State };
        if (row.PostalCode is not null) patch = patch with { PostalCode = row.PostalCode };
        if (row.Country is not null) patch = patch with { Country = row.Country };
        if (row.Gstin is not null) patch = patch with { Gstin = row.Gstin };
        if (row.Tags is not null) patch = patch with { Tags = new(row.Tags) };
        if (row.CustomFields is not null) patch = patch with { CustomFields = new(row.CustomFields) };

        return patch;
    }
}
