namespace SalesApi.Application.Services;
using SalesApi.Application.DTOs;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;

public interface ILossReasonService
{
    Task<List<LossReasonDto>> ListAsync(CancellationToken ct);
    Task<LossReasonDto> CreateAsync(CreateLossReasonRequest request, CancellationToken ct);
    Task<LossReasonDto> UpdateAsync(Guid id, UpdateLossReasonRequest request, CancellationToken ct);
}

public class LossReasonService(
    ILookupRepository lookup,
    IUnitOfWork unitOfWork,
    IRequestContext ctx) : ILossReasonService
{
    public async Task<List<LossReasonDto>> ListAsync(CancellationToken ct)
    {
        var reasons = await lookup.ListLossReasonsAsync(ctx.OrganizationId, null, ct);
        return [.. reasons.Select(ToDto)];
    }

    public async Task<LossReasonDto> CreateAsync(CreateLossReasonRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage loss reasons.");

        var reason = new LossReason
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            Name = request.Name.Trim(),
            IsActive = true
        };

        lookup.AddLossReason(reason);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(reason);
    }

    public async Task<LossReasonDto> UpdateAsync(Guid id, UpdateLossReasonRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage loss reasons.");

        var reason = await lookup.GetLossReasonAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No loss reason with id {id}.");

        if (request.Name is not null) reason.Name = request.Name.Trim();
        if (request.IsActive.HasValue) reason.IsActive = request.IsActive.Value;

        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(reason);
    }

    private static LossReasonDto ToDto(LossReason r)
        => new(r.Id, r.OrganizationId, r.Name, r.IsActive);
}
