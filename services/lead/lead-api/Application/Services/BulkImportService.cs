namespace LeadApi.Application.Services;
using System.Text.Json;
using LeadApi.Application.DTOs;
using LeadApi.Application.Events;
using LeadApi.Application.Normalization;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// Bulk upsert for the data-transfer service (ServiceOnly policy).
/// Matches by email (if present) or phone_normalized, then updates or creates.
/// </summary>
public interface IBulkImportService
{
    Task<BulkUpsertResponse> UpsertAsync(IReadOnlyList<CreateLeadRequest> rows, CancellationToken ct);
}

public class BulkImportService(
    ILeadRepository leads,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<LeadSettings> settings) : IBulkImportService
{
    private readonly LeadSettings _settings = settings.Value;

    public async Task<BulkUpsertResponse> UpsertAsync(IReadOnlyList<CreateLeadRequest> rows, CancellationToken ct)
    {
        if (rows.Count > _settings.MaxImportRowsPerCall)
            throw new LeadValidationException($"Cannot import more than {_settings.MaxImportRowsPerCall} rows per call.");

        var created = 0; var updated = 0; var skipped = 0; var failed = 0;
        var results = new List<BulkUpsertRowResultDto>();

        var now = clock.GetUtcNow();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            try
            {
                if (string.IsNullOrWhiteSpace(row.Email) && string.IsNullOrWhiteSpace(row.Phone))
                {
                    results.Add(new BulkUpsertRowResultDto(index, "failed", null, "A lead must have email or phone."));
                    failed++;
                    continue;
                }

                Lead? existing = null;
                var email = row.Email?.Trim().ToLowerInvariant();
                var phoneNormalized = PhoneNormalizer.Normalize(row.Phone, _settings.DefaultPhoneCountryCode);

                if (!string.IsNullOrWhiteSpace(email))
                    existing = await leads.FindByEmailAsync(ctx.OrganizationId, email, null, ct);

                if (existing is null && phoneNormalized is not null)
                    existing = await leads.FindByPhoneAsync(ctx.OrganizationId, phoneNormalized, null, ct);

                if (existing is not null)
                {
                    // Update
                    if (!string.IsNullOrWhiteSpace(row.FirstName)) existing.FirstName = row.FirstName.Trim();
                    if (!string.IsNullOrWhiteSpace(row.LastName)) existing.LastName = row.LastName.Trim();
                    if (!string.IsNullOrWhiteSpace(row.CompanyName)) existing.CompanyName = row.CompanyName.Trim();
                    if (!string.IsNullOrWhiteSpace(row.JobTitle)) existing.JobTitle = row.JobTitle.Trim();
                    if (!string.IsNullOrWhiteSpace(row.Notes)) existing.Notes = row.Notes.Trim();
                    if (row.LeadSourceId.HasValue) existing.LeadSourceId = row.LeadSourceId.Value;
                    existing.Version += 1;
                    existing.UpdatedAt = now;

                    outbox.Add(EventTypes.LeadUpdated, AggregateTypes.Lead, existing.Id,
                        existing.OrganizationId, existing.Version, ctx.ActorUserId, new { existing.Id, existing.Version });

                    results.Add(new BulkUpsertRowResultDto(index, "updated", existing.Id, null));
                    updated++;
                }
                else
                {
                    // Create
                    var lead = new Lead
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = ctx.OrganizationId,
                        OwnerId = row.OwnerId,
                        FirstName = row.FirstName?.Trim(),
                        LastName = row.LastName?.Trim(),
                        Email = email,
                        Phone = row.Phone?.Trim(),
                        PhoneNormalized = phoneNormalized,
                        CompanyName = row.CompanyName?.Trim(),
                        JobTitle = row.JobTitle?.Trim(),
                        Notes = row.Notes?.Trim(),
                        LeadSourceId = row.LeadSourceId,
                        UtmSource = row.UtmSource?.Trim(),
                        UtmMedium = row.UtmMedium?.Trim(),
                        UtmCampaign = row.UtmCampaign?.Trim(),
                        Tags = row.Tags is null ? [] : [.. row.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant())],
                        CustomFields = row.CustomFields is null ? "{}" : JsonSerializer.Serialize(row.CustomFields),
                        Status = "new",
                        Version = 1,
                        CreatedBy = ctx.ActorUserId,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

                    leads.Add(lead);
                    outbox.Add(EventTypes.LeadCreated, AggregateTypes.Lead, lead.Id,
                        lead.OrganizationId, lead.Version, ctx.ActorUserId, new { lead.Id });

                    results.Add(new BulkUpsertRowResultDto(index, "created", lead.Id, null));
                    created++;
                }
            }
            catch (Exception ex)
            {
                results.Add(new BulkUpsertRowResultDto(index, "failed", null, ex.Message));
                failed++;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new BulkUpsertResponse(created, updated, skipped, failed, results);
    }
}
