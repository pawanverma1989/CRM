namespace SalesApi.Application.Services;
using SalesApi.Application.DTOs;
using SalesApi.Application.Events;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

/// <summary>Pipeline and stage management (PIP-1..PIP-4).</summary>
public interface IPipelineService
{
    Task<List<PipelineDto>> ListAsync(CancellationToken ct);
    Task<PipelineDto> GetAsync(Guid id, CancellationToken ct);
    Task<PipelineDto> CreateAsync(CreatePipelineRequest request, CancellationToken ct);
    Task<PipelineDto> UpdateAsync(Guid id, UpdatePipelineRequest request, CancellationToken ct);
    Task<PipelineStageDto> AddStageAsync(Guid pipelineId, CreateStageRequest request, CancellationToken ct);
    Task<PipelineStageDto> UpdateStageAsync(Guid stageId, UpdateStageRequest request, CancellationToken ct);
    Task ReorderStagesAsync(Guid pipelineId, ReorderStagesRequest request, CancellationToken ct);
}

public class PipelineService(
    ILookupRepository lookup,
    IDealRepository deals,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    ILogger<PipelineService> logger) : IPipelineService
{
    public async Task<List<PipelineDto>> ListAsync(CancellationToken ct)
    {
        var pipelines = await lookup.ListPipelinesAsync(ctx.OrganizationId, ct);
        return [.. pipelines.Select(ToDto)];
    }

    public async Task<PipelineDto> GetAsync(Guid id, CancellationToken ct)
    {
        var pipeline = await lookup.GetPipelineAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No pipeline with id {id}.");
        return ToDto(pipeline);
    }

    public async Task<PipelineDto> CreateAsync(CreatePipelineRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage pipelines.");

        var now = clock.GetUtcNow();
        var pipeline = new Pipeline
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            Name = request.Name.Trim(),
            IsDefault = request.IsDefault,
            CreatedAt = now,
            UpdatedAt = now
        };

        lookup.AddPipeline(pipeline);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Pipeline {Id} '{Name}' created.", pipeline.Id, pipeline.Name);
        return ToDto(pipeline);
    }

    public async Task<PipelineDto> UpdateAsync(Guid id, UpdatePipelineRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage pipelines.");

        var pipeline = await lookup.GetPipelineAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No pipeline with id {id}.");

        if (request.Name is not null) pipeline.Name = request.Name.Trim();
        if (request.IsDefault.HasValue) pipeline.IsDefault = request.IsDefault.Value;
        pipeline.UpdatedAt = clock.GetUtcNow();

        outbox.Add(EventTypes.PipelineUpdated, AggregateTypes.Pipeline, pipeline.Id,
            ctx.OrganizationId, 1, ctx.ActorUserId, new { pipeline_id = pipeline.Id, pipeline.Name });

        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(pipeline);
    }

    public async Task<PipelineStageDto> AddStageAsync(Guid pipelineId, CreateStageRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage pipeline stages.");

        var pipeline = await lookup.GetPipelineAsync(pipelineId, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No pipeline with id {pipelineId}.");

        // PIP-3: enforce one won and one lost per pipeline
        if (request.StageType is "won" or "lost")
        {
            var existingSpecial = pipeline.Stages
                .Any(s => s.StageType == request.StageType && s.IsActive);
            if (existingSpecial)
                throw new ConflictException(
                    $"Pipeline already has an active '{request.StageType}' stage. Only one is allowed (PIP-3).");
        }

        var trimmedName = request.Name.Trim();

        if (pipeline.Stages.Any(s => string.Equals(s.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException($"A stage named '{trimmedName}' already exists in this pipeline.");

        var nextSortOrder = pipeline.Stages.Count == 0 ? 1 : pipeline.Stages.Max(s => s.SortOrder) + 1;

        var stage = new PipelineStage
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            Name = trimmedName,
            SortOrder = nextSortOrder,
            Probability = request.Probability,
            StageType = request.StageType,
            IsActive = true
        };

        lookup.AddStage(stage);
        await unitOfWork.SaveChangesAsync(ct);

        return ToStageDto(stage);
    }

    public async Task<PipelineStageDto> UpdateStageAsync(Guid stageId, UpdateStageRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage pipeline stages.");

        var stage = await lookup.GetStageAsync(stageId, ct)
            ?? throw new NotFoundException($"No stage with id {stageId}.");

        // PIP-4: cannot deactivate a stage that has live deals
        if (request.IsActive == false && stage.IsActive)
        {
            var hasLiveDeals = await deals.HasOpenDealsInStageAsync(stageId, ct);
            if (hasLiveDeals)
                throw new ConflictException(
                    $"Stage '{stage.Name}' has open deals. Move them to another stage before deactivating (PIP-4).");
        }

        if (request.Name is not null) stage.Name = request.Name.Trim();
        if (request.SortOrder.HasValue) stage.SortOrder = request.SortOrder.Value;
        if (request.Probability.HasValue) stage.Probability = request.Probability.Value;
        if (request.IsActive.HasValue) stage.IsActive = request.IsActive.Value;

        await unitOfWork.SaveChangesAsync(ct);
        return ToStageDto(stage);
    }

    public async Task ReorderStagesAsync(Guid pipelineId, ReorderStagesRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may reorder pipeline stages.");

        var pipeline = await lookup.GetPipelineAsync(pipelineId, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No pipeline with id {pipelineId}.");

        var stageIndex = pipeline.Stages.ToDictionary(s => s.Id);

        for (var i = 0; i < request.StageIds.Count; i++)
        {
            if (!stageIndex.TryGetValue(request.StageIds[i], out var stage))
                throw new SalesValidationException("stageIds", $"Stage {request.StageIds[i]} does not belong to pipeline {pipelineId}.");

            stage.SortOrder = i + 1;
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static PipelineDto ToDto(Pipeline p)
        => new(p.Id, p.OrganizationId, p.Name, p.IsDefault, p.CreatedAt, p.UpdatedAt,
               [.. p.Stages.OrderBy(s => s.SortOrder).Select(ToStageDto)]);

    private static PipelineStageDto ToStageDto(PipelineStage s)
        => new(s.Id, s.PipelineId, s.Name, s.SortOrder, s.Probability, s.StageType, s.IsActive);
}
