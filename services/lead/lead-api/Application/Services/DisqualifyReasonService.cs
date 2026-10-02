namespace LeadApi.Application.Services;
using LeadApi.Application.DTOs;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;

public interface IDisqualifyReasonService
{
    Task<List<DisqualifyReasonDto>> ListAsync(bool? isActive, CancellationToken ct);
    Task<DisqualifyReasonDto> CreateAsync(CreateDisqualifyReasonRequest request, CancellationToken ct);
    Task<DisqualifyReasonDto> UpdateAsync(Guid id, UpdateDisqualifyReasonRequest request, CancellationToken ct);
}

public class DisqualifyReasonService(
    ILookupRepository lookups,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock) : IDisqualifyReasonService
{
    public async Task<List<DisqualifyReasonDto>> ListAsync(bool? isActive, CancellationToken ct)
    {
        var reasons = await lookups.ListDisqualifyReasonsAsync(ctx.OrganizationId, isActive, ct);
        return [.. reasons.Select(ToDto)];
    }

    public async Task<DisqualifyReasonDto> CreateAsync(CreateDisqualifyReasonRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage disqualify reasons.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new LeadValidationException("name", "Name is required.");

        var now = clock.GetUtcNow();
        var reason = new DisqualifyReason
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        lookups.AddDisqualifyReason(reason);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(reason);
    }

    public async Task<DisqualifyReasonDto> UpdateAsync(Guid id, UpdateDisqualifyReasonRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage disqualify reasons.");

        var reason = await lookups.GetDisqualifyReasonAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No disqualify reason with id {id}.");

        if (request.Name is not null) reason.Name = request.Name.Trim();
        if (request.IsActive.HasValue) reason.IsActive = request.IsActive.Value;
        reason.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(reason);
    }

    private static DisqualifyReasonDto ToDto(DisqualifyReason r) => new(r.Id, r.Name, r.IsActive);
}
