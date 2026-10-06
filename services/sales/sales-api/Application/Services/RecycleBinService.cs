namespace SalesApi.Application.Services;
using SalesApi.Application.DTOs;
using SalesApi.Application.Events;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

public interface IRecycleBinService
{
    Task<PagedResult<RecycleBinDealDto>> ListAsync(int page, int pageSize, CancellationToken ct);
    Task RestoreAsync(Guid id, CancellationToken ct);
}

public class RecycleBinService(
    IDealRepository deals,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<SalesSettings> settings) : IRecycleBinService
{
    private readonly SalesSettings _settings = settings.Value;

    public async Task<PagedResult<RecycleBinDealDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may access the recycle bin.");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, _settings.MaxPageSize);

        var restorableSince = clock.GetUtcNow().AddDays(-_settings.RecycleBinRetentionDays);

        var total = await deals.CountDeletedAsync(ctx, restorableSince, ct);
        var items = await deals.ListDeletedAsync(ctx, restorableSince, page, pageSize, ct);

        var dtos = items.Select(d => new RecycleBinDealDto(
            d.Id, d.Name, d.OwnerId, d.Status,
            d.DeletedAt!.Value,
            d.DeletedAt.Value.AddDays(_settings.RecycleBinRetentionDays),
            d.Version)).ToList();

        return new PagedResult<RecycleBinDealDto>(dtos, page, pageSize, total);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may restore deals from the recycle bin.");

        var restorableSince = clock.GetUtcNow().AddDays(-_settings.RecycleBinRetentionDays);
        var deal = await deals.GetDeletedAsync(id, ctx, restorableSince, ct)
            ?? throw new NotFoundException($"No restorable deal with id {id}.");

        var now = clock.GetUtcNow();
        deal.DeletedAt = null;
        deal.UpdatedAt = now;

        outbox.Add(EventTypes.DealRestored, AggregateTypes.Deal, deal.Id,
            deal.OrganizationId, deal.Version, ctx.ActorUserId,
            new { deal_id = deal.Id, version = deal.Version });

        await unitOfWork.SaveChangesAsync(ct);
    }
}
