namespace LeadApi.Application.Services;
using LeadApi.Application.DTOs;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;

public interface ILeadSourceService
{
    Task<List<LeadSourceDto>> ListAsync(bool? isActive, CancellationToken ct);
    Task<LeadSourceDto> CreateAsync(CreateLeadSourceRequest request, CancellationToken ct);
    Task<LeadSourceDto> UpdateAsync(Guid id, UpdateLeadSourceRequest request, CancellationToken ct);
}

public class LeadSourceService(
    ILookupRepository lookups,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock) : ILeadSourceService
{
    public async Task<List<LeadSourceDto>> ListAsync(bool? isActive, CancellationToken ct)
    {
        var sources = await lookups.ListLeadSourcesAsync(ctx.OrganizationId, isActive, ct);
        return [.. sources.Select(ToDto)];
    }

    public async Task<LeadSourceDto> CreateAsync(CreateLeadSourceRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage lead sources.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new LeadValidationException("name", "Name is required.");

        var now = clock.GetUtcNow();
        var source = new LeadSource
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        lookups.AddLeadSource(source);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(source);
    }

    public async Task<LeadSourceDto> UpdateAsync(Guid id, UpdateLeadSourceRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage lead sources.");

        var source = await lookups.GetLeadSourceAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No lead source with id {id}.");

        if (request.Name is not null) source.Name = request.Name.Trim();
        if (request.IsActive.HasValue) source.IsActive = request.IsActive.Value;
        source.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(source);
    }

    private static LeadSourceDto ToDto(LeadSource s) => new(s.Id, s.Name, s.IsActive);
}
