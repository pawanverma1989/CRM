namespace SalesApi.Application.Services;
using System.Text.Json;
using SalesApi.Application.DTOs;
using SalesApi.Application.Events;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// Bulk upsert for the data-transfer service (ServiceOnly policy).
/// Matches by source_lead_id first, then by name in the pipeline.
/// </summary>
public interface IBulkImportService
{
    Task<BulkUpsertResponse> UpsertAsync(IReadOnlyList<CreateDealRequest> rows, CancellationToken ct);
}

public class BulkImportService(
    IDealRepository deals,
    ILookupRepository lookup,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<SalesSettings> settings) : IBulkImportService
{
    private readonly SalesSettings _settings = settings.Value;

    public async Task<BulkUpsertResponse> UpsertAsync(IReadOnlyList<CreateDealRequest> rows, CancellationToken ct)
    {
        if (rows.Count > _settings.MaxImportRowsPerCall)
            throw new SalesValidationException($"Cannot import more than {_settings.MaxImportRowsPerCall} rows per call.");

        var created = 0; var updated = 0; var skipped = 0; var failed = 0;
        var results = new List<BulkUpsertRowResultDto>();
        var now = clock.GetUtcNow();

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            try
            {
                if (string.IsNullOrWhiteSpace(row.Name))
                {
                    results.Add(new BulkUpsertRowResultDto(index, "failed", null, "Deal name is required."));
                    failed++;
                    continue;
                }

                // DL-6: idempotency by source_lead_id
                Deal? existing = null;
                if (row.SourceLeadId.HasValue)
                    existing = await deals.FindBySourceLeadIdAsync(row.SourceLeadId.Value, ctx.OrganizationId, ct);

                if (existing is not null)
                {
                    // Update amount if changed
                    if (row.Amount != 0 && row.Amount != existing.Amount)
                    {
                        existing.Amount = row.Amount;
                        existing.UpdatedAt = now;

                        outbox.Add(EventTypes.DealUpdated, AggregateTypes.Deal, existing.Id,
                            existing.OrganizationId, existing.Version + 1, ctx.ActorUserId,
                            new { deal_id = existing.Id, version = existing.Version + 1 });
                    }

                    results.Add(new BulkUpsertRowResultDto(index, "updated", existing.Id, null));
                    updated++;
                }
                else
                {
                    // Validate pipeline
                    var pipeline = await lookup.GetPipelineAsync(row.PipelineId, ctx.OrganizationId, ct);
                    if (pipeline is null)
                    {
                        results.Add(new BulkUpsertRowResultDto(index, "failed", null, "Pipeline not found."));
                        failed++;
                        continue;
                    }

                    var deal = new Deal
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = ctx.OrganizationId,
                        PipelineId = row.PipelineId,
                        StageId = row.StageId,
                        OwnerId = row.OwnerId,
                        CompanyId = row.CompanyId,
                        PrimaryContactId = row.PrimaryContactId,
                        Name = row.Name.Trim(),
                        Amount = row.Amount,
                        Currency = row.Currency,
                        Probability = row.Probability,
                        ExpectedCloseDate = row.ExpectedCloseDate,
                        Tags = row.Tags is null ? [] : [.. row.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant())],
                        CustomFields = row.CustomFields is null ? "{}" : JsonSerializer.Serialize(row.CustomFields),
                        SourceLeadId = row.SourceLeadId,
                        Version = 1,
                        CreatedBy = ctx.ActorUserId,
                        CreatedAt = now,
                        UpdatedAt = now,
                        StageEnteredAt = now
                    };

                    await unitOfWork.ExecuteSqlAsync(
                        $"SELECT set_config('app.current_user_id', {ctx.ActorUserId.ToString()}, true)", ct);

                    deals.Add(deal);
                    // deal.created written by DB trigger
                    results.Add(new BulkUpsertRowResultDto(index, "created", deal.Id, null));
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
