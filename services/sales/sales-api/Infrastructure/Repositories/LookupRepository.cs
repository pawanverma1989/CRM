namespace SalesApi.Infrastructure.Repositories;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Reads reference lists: pipelines, stages, loss reasons, customer_refs, user_refs,
/// and custom field definitions.
/// </summary>
public interface ILookupRepository
{
    // Pipelines
    Task<Pipeline?> GetPipelineAsync(Guid id, Guid organizationId, CancellationToken ct);
    Task<List<Pipeline>> ListPipelinesAsync(Guid organizationId, CancellationToken ct);
    Task<Pipeline?> GetDefaultPipelineAsync(Guid organizationId, CancellationToken ct);
    void AddPipeline(Pipeline pipeline);

    // Stages
    Task<PipelineStage?> GetStageAsync(Guid id, CancellationToken ct);
    Task<List<PipelineStage>> ListStagesAsync(Guid pipelineId, CancellationToken ct);
    void AddStage(PipelineStage stage);

    // Loss reasons
    Task<LossReason?> GetLossReasonAsync(Guid id, Guid organizationId, CancellationToken ct);
    Task<List<LossReason>> ListLossReasonsAsync(Guid organizationId, bool? isActive, CancellationToken ct);
    void AddLossReason(LossReason reason);

    // Custom fields
    Task<List<CustomFieldDefinition>> ListCustomFieldsAsync(Guid organizationId, string entityType, CancellationToken ct);
    Task<CustomFieldDefinition?> GetCustomFieldAsync(Guid id, Guid organizationId, CancellationToken ct);
    Task<int> CountActiveCustomFieldsAsync(Guid organizationId, string entityType, CancellationToken ct);
    void AddCustomField(CustomFieldDefinition field);

    // Customer refs (local copies from Customer service)
    Task<CustomerRef?> GetCustomerRefAsync(string entityType, Guid id, CancellationToken ct);
    void UpsertCustomerRef(CustomerRef customerRef);

    // User refs
    Task<List<UserRef>> OwnersAsync(Guid organizationId, bool activeOnly, CancellationToken ct);
    Task<UserRef?> GetUserRefAsync(Guid userId, CancellationToken ct);
    void AddUserRef(UserRef userRef);
}

public class LookupRepository(SalesDbContext context) : ILookupRepository
{
    public Task<Pipeline?> GetPipelineAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.Pipelines
            .Include(p => p.Stages)
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId, ct);

    public Task<List<Pipeline>> ListPipelinesAsync(Guid organizationId, CancellationToken ct)
        => context.Pipelines
            .Include(p => p.Stages.OrderBy(s => s.SortOrder))
            .Where(e => e.OrganizationId == organizationId)
            .OrderBy(e => e.Name)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<Pipeline?> GetDefaultPipelineAsync(Guid organizationId, CancellationToken ct)
        => context.Pipelines
            .Include(p => p.Stages.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(e => e.OrganizationId == organizationId && e.IsDefault, ct);

    public void AddPipeline(Pipeline pipeline) => context.Pipelines.Add(pipeline);

    public Task<PipelineStage?> GetStageAsync(Guid id, CancellationToken ct)
        => context.PipelineStages.FindAsync([id], ct).AsTask();

    public Task<List<PipelineStage>> ListStagesAsync(Guid pipelineId, CancellationToken ct)
        => context.PipelineStages
            .Where(e => e.PipelineId == pipelineId)
            .OrderBy(e => e.SortOrder)
            .AsNoTracking()
            .ToListAsync(ct);

    public void AddStage(PipelineStage stage) => context.PipelineStages.Add(stage);

    public Task<LossReason?> GetLossReasonAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.LossReasons
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId, ct);

    public Task<List<LossReason>> ListLossReasonsAsync(Guid organizationId, bool? isActive, CancellationToken ct)
    {
        var q = context.LossReasons.Where(e => e.OrganizationId == organizationId);
        if (isActive.HasValue) q = q.Where(e => e.IsActive == isActive.Value);
        return q.OrderBy(e => e.Name).AsNoTracking().ToListAsync(ct);
    }

    public void AddLossReason(LossReason reason) => context.LossReasons.Add(reason);

    public Task<List<CustomFieldDefinition>> ListCustomFieldsAsync(Guid organizationId, string entityType, CancellationToken ct)
        => context.CustomFieldDefinitions
            .Where(e => e.OrganizationId == organizationId && e.EntityType == entityType)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Label)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<CustomFieldDefinition?> GetCustomFieldAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.CustomFieldDefinitions
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId, ct);

    public Task<int> CountActiveCustomFieldsAsync(Guid organizationId, string entityType, CancellationToken ct)
        => context.CustomFieldDefinitions
            .CountAsync(e => e.OrganizationId == organizationId && e.EntityType == entityType && e.IsActive, ct);

    public void AddCustomField(CustomFieldDefinition field) => context.CustomFieldDefinitions.Add(field);

    public Task<CustomerRef?> GetCustomerRefAsync(string entityType, Guid id, CancellationToken ct)
        => context.CustomerRefs
            .FirstOrDefaultAsync(e => e.EntityType == entityType && e.Id == id, ct);

    public void UpsertCustomerRef(CustomerRef customerRef)
    {
        var existing = context.CustomerRefs.Find(customerRef.EntityType, customerRef.Id);
        if (existing is null)
            context.CustomerRefs.Add(customerRef);
        else
        {
            existing.DisplayName = customerRef.DisplayName;
            existing.Email = customerRef.Email;
            existing.IsDeleted = customerRef.IsDeleted;
            existing.SourceVersion = customerRef.SourceVersion;
            existing.UpdatedAt = customerRef.UpdatedAt;
        }
    }

    public Task<List<UserRef>> OwnersAsync(Guid organizationId, bool activeOnly, CancellationToken ct)
    {
        var q = context.UserRefs.Where(u => u.OrganizationId == organizationId);
        if (activeOnly) q = q.Where(u => u.IsActive);
        return q.OrderBy(u => u.DisplayName).AsNoTracking().ToListAsync(ct);
    }

    public Task<UserRef?> GetUserRefAsync(Guid userId, CancellationToken ct)
        => context.UserRefs.FindAsync([userId], ct).AsTask();

    public void AddUserRef(UserRef userRef) => context.UserRefs.Add(userRef);
}
