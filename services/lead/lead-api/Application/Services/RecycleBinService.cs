namespace LeadApi.Application.Services;
using LeadApi.Application.DTOs;
using LeadApi.Application.Events;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

public interface IRecycleBinService
{
    Task<PagedResult<RecycleBinLeadDto>> ListAsync(int page, int pageSize, CancellationToken ct);
    Task<LeadDto> RestoreAsync(Guid id, CancellationToken ct);
}

public class RecycleBinService(
    ILeadRepository leads,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<LeadSettings> settings) : IRecycleBinService
{
    private readonly LeadSettings _settings = settings.Value;

    public async Task<PagedResult<RecycleBinLeadDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may access the recycle bin.");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, _settings.MaxPageSize);

        var restorableSince = clock.GetUtcNow().AddDays(-_settings.RecycleBinRetentionDays);

        var total = await leads.CountDeletedAsync(ctx, restorableSince, ct);
        var items = await leads.ListDeletedAsync(ctx, restorableSince, page, pageSize, ct);
        var purgeAfter = clock.GetUtcNow().AddDays(_settings.RecycleBinRetentionDays);

        var dtos = items.Select(l => new RecycleBinLeadDto(
            l.Id, l.FirstName, l.LastName, l.Email, l.Phone, l.OwnerId,
            l.DeletedAt!.Value,
            l.DeletedAt.Value.AddDays(_settings.RecycleBinRetentionDays),
            l.Version)).ToList();

        return new PagedResult<RecycleBinLeadDto>(dtos, page, pageSize, total);
    }

    public async Task<LeadDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may restore leads from the recycle bin.");

        var restorableSince = clock.GetUtcNow().AddDays(-_settings.RecycleBinRetentionDays);
        var lead = await leads.GetDeletedAsync(id, ctx, restorableSince, ct)
            ?? throw new NotFoundException($"No restorable lead with id {id}.");

        var now = clock.GetUtcNow();
        lead.DeletedAt = null;
        lead.Version += 1;
        lead.UpdatedAt = now;

        outbox.Add(EventTypes.LeadRestored, AggregateTypes.Lead, lead.Id,
            lead.OrganizationId, lead.Version, ctx.ActorUserId,
            new { lead_id = lead.Id, version = lead.Version });

        await unitOfWork.SaveChangesAsync(ct);

        return new LeadDto(
            lead.Id, lead.OrganizationId, lead.OwnerId,
            lead.FirstName, lead.LastName, lead.Email, lead.Phone,
            lead.CompanyName, lead.JobTitle, lead.Status,
            lead.DisqualifyReason, lead.DisqualifyReasonId,
            lead.LeadSourceId, null, lead.WebFormId,
            lead.UtmSource, lead.UtmMedium, lead.UtmCampaign, lead.Notes,
            lead.Tags, null,
            lead.ConvertedAt, lead.ConvertedContactId, lead.ConvertedCompanyId, lead.ConvertedDealId,
            lead.Version, lead.CreatedBy, lead.CreatedAt, lead.UpdatedAt);
    }
}
