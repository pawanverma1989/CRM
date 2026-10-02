namespace LeadApi.Infrastructure.Repositories;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Reads reference lists that don't belong to the core lead aggregate: sources, disqualify reasons,
/// custom field definitions, and web forms.
/// </summary>
public interface ILookupRepository
{
    Task<LeadSource?> GetLeadSourceAsync(Guid id, Guid organizationId, CancellationToken ct);
    Task<List<LeadSource>> ListLeadSourcesAsync(Guid organizationId, bool? isActive, CancellationToken ct);
    Task<DisqualifyReason?> GetDisqualifyReasonAsync(Guid id, Guid organizationId, CancellationToken ct);
    Task<List<DisqualifyReason>> ListDisqualifyReasonsAsync(Guid organizationId, bool? isActive, CancellationToken ct);
    Task<WebForm?> GetWebFormAsync(Guid id, Guid organizationId, CancellationToken ct);
    Task<WebForm?> GetWebFormByPublicKeyAsync(string publicKey, CancellationToken ct);
    Task<List<WebForm>> ListWebFormsAsync(Guid organizationId, CancellationToken ct);
    Task<List<CustomFieldDefinition>> ListCustomFieldsAsync(Guid organizationId, string entityType, CancellationToken ct);
    Task<CustomFieldDefinition?> GetCustomFieldAsync(Guid id, Guid organizationId, CancellationToken ct);
    Task<int> CountActiveCustomFieldsAsync(Guid organizationId, string entityType, CancellationToken ct);
    Task<List<UserRef>> OwnersAsync(Guid organizationId, bool activeOnly, CancellationToken ct);
    Task<UserRef?> GetUserRefAsync(Guid userId, CancellationToken ct);
    void AddLeadSource(LeadSource source);
    void AddDisqualifyReason(DisqualifyReason reason);
    void AddWebForm(WebForm form);
    void AddCustomField(CustomFieldDefinition field);
    void AddUserRef(UserRef userRef);
}

public class LookupRepository(LeadDbContext context) : ILookupRepository
{
    public Task<LeadSource?> GetLeadSourceAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.LeadSources
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId, ct);

    public Task<List<LeadSource>> ListLeadSourcesAsync(Guid organizationId, bool? isActive, CancellationToken ct)
    {
        var q = context.LeadSources.Where(e => e.OrganizationId == organizationId);
        if (isActive.HasValue) q = q.Where(e => e.IsActive == isActive.Value);
        return q.OrderBy(e => e.Name).AsNoTracking().ToListAsync(ct);
    }

    public Task<DisqualifyReason?> GetDisqualifyReasonAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.DisqualifyReasons
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId, ct);

    public Task<List<DisqualifyReason>> ListDisqualifyReasonsAsync(Guid organizationId, bool? isActive, CancellationToken ct)
    {
        var q = context.DisqualifyReasons.Where(e => e.OrganizationId == organizationId);
        if (isActive.HasValue) q = q.Where(e => e.IsActive == isActive.Value);
        return q.OrderBy(e => e.Name).AsNoTracking().ToListAsync(ct);
    }

    public Task<WebForm?> GetWebFormAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.WebForms
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == organizationId, ct);

    public Task<WebForm?> GetWebFormByPublicKeyAsync(string publicKey, CancellationToken ct)
        => context.WebForms
            .FirstOrDefaultAsync(e => e.PublicKey == publicKey && e.IsActive, ct);

    public Task<List<WebForm>> ListWebFormsAsync(Guid organizationId, CancellationToken ct)
        => context.WebForms
            .Where(e => e.OrganizationId == organizationId)
            .OrderBy(e => e.Name)
            .AsNoTracking()
            .ToListAsync(ct);

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

    public Task<List<UserRef>> OwnersAsync(Guid organizationId, bool activeOnly, CancellationToken ct)
    {
        var q = context.UserRefs.Where(u => u.OrganizationId == organizationId);
        if (activeOnly) q = q.Where(u => u.IsActive);
        return q.OrderBy(u => u.DisplayName).AsNoTracking().ToListAsync(ct);
    }

    public Task<UserRef?> GetUserRefAsync(Guid userId, CancellationToken ct)
        => context.UserRefs.FindAsync([userId], ct).AsTask();

    public void AddLeadSource(LeadSource source) => context.LeadSources.Add(source);
    public void AddDisqualifyReason(DisqualifyReason reason) => context.DisqualifyReasons.Add(reason);
    public void AddWebForm(WebForm form) => context.WebForms.Add(form);
    public void AddCustomField(CustomFieldDefinition field) => context.CustomFieldDefinitions.Add(field);
    public void AddUserRef(UserRef userRef) => context.UserRefs.Add(userRef);
}
