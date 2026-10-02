namespace LeadApi.Application.Services;
using LeadApi.Application.DTOs;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Exports leads for the data-transfer service (ServiceOnly policy, paged).
/// </summary>
public interface IExportService
{
    Task<PagedResult<LeadDto>> ExportAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct);
}

public class ExportService(
    LeadDbContext context,
    IRequestContext ctx,
    IOptions<LeadSettings> settings) : IExportService
{
    private readonly LeadSettings _settings = settings.Value;

    public async Task<PagedResult<LeadDto>> ExportAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, _settings.MaxPageSize);

        var query = context.Leads
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

        var dtos = items.Select(l => new LeadDto(
            l.Id, l.OrganizationId, l.OwnerId,
            l.FirstName, l.LastName, l.Email, l.Phone,
            l.CompanyName, l.JobTitle, l.Status,
            l.DisqualifyReason, l.DisqualifyReasonId,
            l.LeadSourceId, null, l.WebFormId,
            l.UtmSource, l.UtmMedium, l.UtmCampaign, l.Notes,
            l.Tags, null,
            l.ConvertedAt, l.ConvertedContactId, l.ConvertedCompanyId, l.ConvertedDealId,
            l.Version, l.CreatedBy, l.CreatedAt, l.UpdatedAt)).ToList();

        return new PagedResult<LeadDto>(dtos, page, pageSize, total);
    }
}
