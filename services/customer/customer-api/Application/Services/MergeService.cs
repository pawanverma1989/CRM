namespace CustomerApi.Application.Services;
using System.Text.Json;
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
using Microsoft.Extensions.Options;

/// <summary>
/// DUP-4..DUP-6. The whole merge — the survivor's new values, the losing record's
/// <c>merged_into_id</c> and <c>deleted_at</c>, the <c>merge_history</c> row and every event — is
/// one database transaction (NFR-5). A merge cannot be undone (DUP-6).
/// </summary>
public interface IMergeService
{
    Task<MergeResultDto> MergeCompaniesAsync(MergeRequest request, CancellationToken ct);
    Task<MergeResultDto> MergeContactsAsync(MergeRequest request, CancellationToken ct);
}

public class MergeService(
    ICompanyRepository companies,
    IContactRepository contacts,
    ILookupRepository lookups,
    CustomerDbContext context,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<CustomerSettings> settings) : IMergeService
{
    private readonly CustomerSettings _settings = settings.Value;

    public async Task<MergeResultDto> MergeCompaniesAsync(MergeRequest request, CancellationToken ct)
    {
        EnsureDistinct(request);

        var survivor = await companies.GetAsync(request.SurvivorId, ctx, ct)
            ?? throw new NotFoundException($"No company with id {request.SurvivorId}.");
        var loser = await companies.GetAsync(request.LoserId, ctx, ct)
            ?? throw new NotFoundException($"No company with id {request.LoserId}.");

        var now = clock.GetUtcNow();
        var choices = Normalize(request.FieldChoices);

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        // The losing record leaves uq_companies_domain first, so the survivor may take its domain
        // in the second statement without tripping the unique index.
        loser.MergedIntoId = survivor.Id;
        loser.DeletedAt = now;
        loser.Version += 1;
        loser.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        var before = RecordSnapshot.Of(survivor);

        if (Takes(choices, "name") && !string.IsNullOrWhiteSpace(loser.Name)) survivor.Name = loser.Name;
        if (Takes(choices, "domain")) survivor.Domain = loser.Domain;
        if (Takes(choices, "industryId")) survivor.IndustryId = loser.IndustryId;
        if (Takes(choices, "employeeCount")) survivor.EmployeeCount = loser.EmployeeCount;
        if (Takes(choices, "annualRevenue")) survivor.AnnualRevenue = loser.AnnualRevenue;
        if (Takes(choices, "phone")) survivor.Phone = loser.Phone;
        if (Takes(choices, "website")) survivor.Website = loser.Website;
        if (Takes(choices, "addressLine1")) survivor.AddressLine1 = loser.AddressLine1;
        if (Takes(choices, "addressLine2")) survivor.AddressLine2 = loser.AddressLine2;
        if (Takes(choices, "city")) survivor.City = loser.City;
        if (Takes(choices, "state")) survivor.State = loser.State;
        if (Takes(choices, "postalCode")) survivor.PostalCode = loser.PostalCode;
        if (Takes(choices, "country")) survivor.Country = loser.Country;
        if (Takes(choices, "gstin")) survivor.Gstin = loser.Gstin;
        if (Takes(choices, "ownerId")) survivor.OwnerId = loser.OwnerId;
        if (Takes(choices, "customFields")) survivor.CustomFields = loser.CustomFields;

        // DUP-5: tags are combined, not chosen.
        survivor.Tags = TagNormalizer.Normalize(
            survivor.Tags.Concat(loser.Tags), _settings.MaxTagsPerRecord);

        // DUP-5: the losing company's contacts move to the survivor.
        var moved = await contacts.LinkedToCompanyAsync(loser.Id, ctx.OrganizationId, ct);
        foreach (var contact in moved)
        {
            var contactBefore = RecordSnapshot.Of(contact);
            contact.CompanyId = survivor.Id;
            contact.Version += 1;
            contact.UpdatedAt = now;

            outbox.Add(EventTypes.ContactUpdated, AggregateTypes.Contact, contact.Id,
                contact.OrganizationId, contact.Version, ctx.ActorUserId,
                new UpdatedEventPayload<ContactDto>(
                    CustomerMapper.ToDto(contact, LookupNames.Empty),
                    RecordSnapshot.Diff(contactBefore, RecordSnapshot.Of(contact))));
        }

        survivor.Version += 1;
        survivor.UpdatedAt = now;

        var movedIds = moved.Select(c => c.Id).ToArray();
        RecordMerge(EntityTypes.Company, survivor.Id, loser.Id, choices, now);

        var dto = await ToDtoAsync(survivor, ct);
        var changes = RecordSnapshot.Diff(before, RecordSnapshot.Of(survivor));
        if (changes.Count > 0)
            outbox.Add(EventTypes.CompanyUpdated, AggregateTypes.Company, survivor.Id,
                survivor.OrganizationId, survivor.Version, ctx.ActorUserId,
                new UpdatedEventPayload<CompanyDto>(dto, changes));

        outbox.Add(EventTypes.CompanyMerged, AggregateTypes.Company, survivor.Id,
            survivor.OrganizationId, survivor.Version, ctx.ActorUserId,
            new MergedEventPayload(survivor.Id, loser.Id, movedIds, survivor.Tags));

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new MergeResultDto(survivor.Id, loser.Id, movedIds, survivor.Tags, survivor.Version);
    }

    public async Task<MergeResultDto> MergeContactsAsync(MergeRequest request, CancellationToken ct)
    {
        EnsureDistinct(request);

        var survivor = await contacts.GetAsync(request.SurvivorId, ctx, ct)
            ?? throw new NotFoundException($"No contact with id {request.SurvivorId}.");
        var loser = await contacts.GetAsync(request.LoserId, ctx, ct)
            ?? throw new NotFoundException($"No contact with id {request.LoserId}.");

        var now = clock.GetUtcNow();
        var choices = Normalize(request.FieldChoices);

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        // Out of uq_contacts_email first, so the survivor may take the losing contact's email.
        loser.MergedIntoId = survivor.Id;
        loser.DeletedAt = now;
        loser.Version += 1;
        loser.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        var before = RecordSnapshot.Of(survivor);

        if (Takes(choices, "firstName") && !string.IsNullOrWhiteSpace(loser.FirstName))
            survivor.FirstName = loser.FirstName;
        if (Takes(choices, "lastName")) survivor.LastName = loser.LastName;
        if (Takes(choices, "email")) survivor.Email = loser.Email;
        if (Takes(choices, "phone"))
        {
            survivor.Phone = loser.Phone;
            survivor.PhoneNormalized = loser.PhoneNormalized;
        }
        if (Takes(choices, "mobile")) survivor.Mobile = loser.Mobile;
        if (Takes(choices, "jobTitle")) survivor.JobTitle = loser.JobTitle;
        if (Takes(choices, "companyId")) survivor.CompanyId = loser.CompanyId;
        if (Takes(choices, "addressLine1")) survivor.AddressLine1 = loser.AddressLine1;
        if (Takes(choices, "addressLine2")) survivor.AddressLine2 = loser.AddressLine2;
        if (Takes(choices, "city")) survivor.City = loser.City;
        if (Takes(choices, "state")) survivor.State = loser.State;
        if (Takes(choices, "postalCode")) survivor.PostalCode = loser.PostalCode;
        if (Takes(choices, "country")) survivor.Country = loser.Country;
        if (Takes(choices, "sourceId")) survivor.SourceId = loser.SourceId;
        if (Takes(choices, "ownerId")) survivor.OwnerId = loser.OwnerId;
        if (Takes(choices, "customFields")) survivor.CustomFields = loser.CustomFields;

        survivor.Tags = TagNormalizer.Normalize(
            survivor.Tags.Concat(loser.Tags), _settings.MaxTagsPerRecord);

        // CON-1 is still a database CHECK after a merge; keep an email or a phone on the survivor.
        if (survivor.Email is null && survivor.Phone is null)
            throw new CustomerValidationException(
                "email", "The surviving contact must keep an email or a phone number.");

        survivor.Version += 1;
        survivor.UpdatedAt = now;

        RecordMerge(EntityTypes.Contact, survivor.Id, loser.Id, choices, now);

        var dto = await ToDtoAsync(survivor, ct);
        var changes = RecordSnapshot.Diff(before, RecordSnapshot.Of(survivor));
        if (changes.Count > 0)
            outbox.Add(EventTypes.ContactUpdated, AggregateTypes.Contact, survivor.Id,
                survivor.OrganizationId, survivor.Version, ctx.ActorUserId,
                new UpdatedEventPayload<ContactDto>(dto, changes));

        // DUP-5 / AC-9: Sales, Activity, Lead and Search re-point their links on this event.
        outbox.Add(EventTypes.ContactMerged, AggregateTypes.Contact, survivor.Id,
            survivor.OrganizationId, survivor.Version, ctx.ActorUserId,
            new MergedEventPayload(survivor.Id, loser.Id, [], survivor.Tags));

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new MergeResultDto(survivor.Id, loser.Id, [], survivor.Tags, survivor.Version);
    }

    private void RecordMerge(
        string entityType, Guid survivorId, Guid loserId,
        IReadOnlyDictionary<string, string> choices, DateTimeOffset now)
        => context.MergeHistories.Add(new MergeHistory
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            EntityType = entityType,
            SurvivorId = survivorId,
            LoserId = loserId,
            FieldChoices = JsonSerializer.Serialize(choices),
            MergedBy = ctx.ActorUserId,
            MergedAt = now
        });

    private static void EnsureDistinct(MergeRequest request)
    {
        if (request.SurvivorId == Guid.Empty || request.LoserId == Guid.Empty)
            throw new CustomerValidationException("survivorId", "Both survivorId and loserId are required.");

        if (request.SurvivorId == request.LoserId)
            throw new CustomerValidationException("loserId", "A record cannot be merged into itself.");
    }

    /// <summary>DUP-4: anything the caller did not choose keeps the survivor's value.</summary>
    private static Dictionary<string, string> Normalize(Dictionary<string, string>? choices)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, source) in choices ?? [])
        {
            var key = field.Replace("_", string.Empty);
            result[key] = source.Trim().ToLowerInvariant();
        }
        return result;
    }

    private static bool Takes(IReadOnlyDictionary<string, string> choices, string field)
        => choices.TryGetValue(field, out var source) && source == "loser";

    private async Task<CompanyDto> ToDtoAsync(Company company, CancellationToken ct)
    {
        var names = await lookups.NamesAsync(
            ctx.OrganizationId,
            company.OwnerId is null ? [] : [company.OwnerId.Value],
            company.IndustryId is null ? [] : [company.IndustryId.Value],
            [], ct);
        return CustomerMapper.ToDto(company, names);
    }

    private async Task<ContactDto> ToDtoAsync(Contact contact, CancellationToken ct)
    {
        var names = await lookups.NamesAsync(
            ctx.OrganizationId,
            contact.OwnerId is null ? [] : [contact.OwnerId.Value],
            contact.SourceId is null ? [] : [contact.SourceId.Value],
            contact.CompanyId is null ? [] : [contact.CompanyId.Value], ct);
        return CustomerMapper.ToDto(contact, names);
    }
}
