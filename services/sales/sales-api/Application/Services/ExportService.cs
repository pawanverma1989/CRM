namespace SalesApi.Application.Services;
using System.Text.Json;
using SalesApi.Application.DTOs;
using SalesApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Exports deals for the data-transfer service (ServiceOnly policy, paged).
/// </summary>
public interface IExportService
{
    Task<PagedResult<DealDto>> ExportAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct);
}

public class ExportService(
    SalesDbContext context,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<SalesSettings> settings) : IExportService
{
    private readonly SalesSettings _settings = settings.Value;

    public async Task<PagedResult<DealDto>> ExportAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, _settings.MaxPageSize);

        var query = context.Deals
            .Where(e => e.OrganizationId == ctx.OrganizationId && e.DeletedAt == null);

        if (updatedSince.HasValue)
            query = query.Where(e => e.UpdatedAt >= updatedSince.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.UpdatedAt).ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var dtos = items.Select(d =>
        {
            var isStale = (d.Status == "open") &&
                ((d.LastActivityAt.HasValue && (now - d.LastActivityAt.Value).TotalDays > _settings.StaleDaysWithoutActivity)
                 || (now - d.StageEnteredAt).TotalDays > _settings.StaleDaysInStage);
            var isOverdue = d.ExpectedCloseDate.HasValue && d.Status == "open" && d.ExpectedCloseDate.Value < today;

            CustomFieldValues? cfv = null;
            if (!string.IsNullOrWhiteSpace(d.CustomFields) && d.CustomFields != "{}")
            {
                try { cfv = JsonSerializer.Deserialize<CustomFieldValues>(d.CustomFields); } catch { }
            }

            return new DealDto(
                d.Id, d.OrganizationId,
                d.PipelineId, null, d.StageId, null,
                d.OwnerId, null,
                d.CompanyId, null,
                d.PrimaryContactId, null,
                d.Name, d.Amount, d.Currency,
                d.Probability, d.ExpectedCloseDate,
                d.Status, d.ClosedAt,
                d.LossReasonId, null, d.LossNotes,
                d.SourceLeadId,
                d.StageEnteredAt, d.LastActivityAt,
                d.Tags, cfv,
                d.Version, d.CreatedBy, d.CreatedAt, d.UpdatedAt,
                null, null, isStale, isOverdue);
        }).ToList();

        return new PagedResult<DealDto>(dtos, page, pageSize, total);
    }
}
