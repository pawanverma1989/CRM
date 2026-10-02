namespace LeadApi.Application.Services;
using System.Text.Json;
using LeadApi.Application.DTOs;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;

public interface ICustomFieldService
{
    Task<List<CustomFieldDefinitionDto>> ListAsync(string entityType, CancellationToken ct);
    Task<CustomFieldDefinitionDto> CreateAsync(CreateCustomFieldRequest request, CancellationToken ct);
    Task<CustomFieldDefinitionDto> UpdateAsync(Guid id, UpdateCustomFieldRequest request, CancellationToken ct);
}

public class CustomFieldService(
    ILookupRepository lookups,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<LeadSettings> settings) : ICustomFieldService
{
    private readonly LeadSettings _settings = settings.Value;

    public async Task<List<CustomFieldDefinitionDto>> ListAsync(string entityType, CancellationToken ct)
    {
        var fields = await lookups.ListCustomFieldsAsync(ctx.OrganizationId, entityType, ct);
        return [.. fields.Select(ToDto)];
    }

    public async Task<CustomFieldDefinitionDto> CreateAsync(CreateCustomFieldRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage custom field definitions.");

        if (string.IsNullOrWhiteSpace(request.FieldKey))
            throw new LeadValidationException("fieldKey", "Field key is required.");

        if (!System.Text.RegularExpressions.Regex.IsMatch(request.FieldKey, @"^[a-z][a-z0-9_]*$"))
            throw new LeadValidationException("fieldKey", "Field key must match ^[a-z][a-z0-9_]*$.");

        var activeCount = await lookups.CountActiveCustomFieldsAsync(ctx.OrganizationId, request.EntityType, ct);
        if (activeCount >= _settings.MaxActiveCustomFields)
            throw new LeadValidationException(
                $"You have reached the maximum of {_settings.MaxActiveCustomFields} active custom fields.");

        var now = clock.GetUtcNow();
        var field = new CustomFieldDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            EntityType = request.EntityType,
            FieldKey = request.FieldKey,
            Label = request.Label,
            FieldType = request.FieldType,
            Options = request.Options is not null ? JsonSerializer.Serialize(request.Options) : null,
            IsRequired = request.IsRequired,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        lookups.AddCustomField(field);
        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(field);
    }

    public async Task<CustomFieldDefinitionDto> UpdateAsync(Guid id, UpdateCustomFieldRequest request, CancellationToken ct)
    {
        if (!ctx.IsAdmin)
            throw new ForbiddenException("Only admins may manage custom field definitions.");

        var field = await lookups.GetCustomFieldAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No custom field definition with id {id}.");

        if (request.Label is not null) field.Label = request.Label;
        if (request.Options is not null) field.Options = JsonSerializer.Serialize(request.Options);
        if (request.IsRequired.HasValue) field.IsRequired = request.IsRequired.Value;
        if (request.SortOrder.HasValue) field.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue) field.IsActive = request.IsActive.Value;
        field.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.SaveChangesAsync(ct);
        return ToDto(field);
    }

    private static CustomFieldDefinitionDto ToDto(CustomFieldDefinition f)
    {
        IReadOnlyList<string>? options = null;
        if (!string.IsNullOrWhiteSpace(f.Options))
        {
            try { options = JsonSerializer.Deserialize<List<string>>(f.Options); }
            catch { options = null; }
        }

        return new CustomFieldDefinitionDto(f.Id, f.EntityType, f.FieldKey, f.Label, f.FieldType, options, f.IsRequired, f.SortOrder, f.IsActive);
    }
}
