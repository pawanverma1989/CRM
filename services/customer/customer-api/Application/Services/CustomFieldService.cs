namespace CustomerApi.Application.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Mapping;
using CustomerApi.Application.Validation;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;

/// <summary>CF-1..CF-7. Stored values are never deleted, so a reactivated field shows them again (CF-5, AC-11).</summary>
public interface ICustomFieldService
{
    Task<IReadOnlyList<CustomFieldDefinitionDto>> ListAsync(string entityType, bool includeInactive, CancellationToken ct);
    Task<CustomFieldDefinitionDto> CreateAsync(CreateCustomFieldRequest request, CancellationToken ct);
    Task<CustomFieldDefinitionDto> UpdateAsync(Guid id, UpdateCustomFieldRequest request, CancellationToken ct);
}

public partial class CustomFieldService(
    ILookupRepository lookups,
    CustomerDbContext context,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<CustomerSettings> settings) : ICustomFieldService
{
    private readonly CustomerSettings _settings = settings.Value;

    /// <summary>The <c>field_key</c> format the V1 CHECK enforces.</summary>
    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldKeyPattern();

    public async Task<IReadOnlyList<CustomFieldDefinitionDto>> ListAsync(
        string entityType, bool includeInactive, CancellationToken ct)
    {
        var type = EntityTypes.Require(entityType);

        // CF-5: a deactivated field disappears from forms, so only an admin may ask to see it.
        var withInactive = includeInactive && ctx.IsAdmin;

        var definitions = await lookups.CustomFieldsAsync(ctx.OrganizationId, type, withInactive, ct);
        return [.. definitions.Select(CustomerMapper.ToDto)];
    }

    public async Task<CustomFieldDefinitionDto> CreateAsync(
        CreateCustomFieldRequest request, CancellationToken ct)
    {
        var entityType = EntityTypes.Require(request.EntityType);
        var fieldKey = (request.FieldKey ?? string.Empty).Trim().ToLowerInvariant();
        var label = (request.Label ?? string.Empty).Trim();
        var fieldType = (request.FieldType ?? string.Empty).Trim().ToLowerInvariant();

        if (!FieldKeyPattern().IsMatch(fieldKey))
            throw new CustomerValidationException("fieldKey",
                "A key starts with a lower-case letter and may contain only lower-case letters, digits and underscores.");

        if (label.Length == 0) throw new CustomerValidationException("label", "A label is required.");

        CustomFieldValidator.ValidateDefinition(fieldType, request.Options);

        // CF-7: at most Customer:MaxActiveCustomFields active fields per record type.
        var active = await lookups.ActiveCustomFieldCountAsync(ctx.OrganizationId, entityType, ct);
        if (active >= _settings.MaxActiveCustomFields)
            throw new CustomerValidationException("entityType",
                $"A {entityType} may have at most {_settings.MaxActiveCustomFields} active custom fields.");

        var existing = (await lookups.CustomFieldsAsync(ctx.OrganizationId, entityType, includeInactive: true, ct))
            .FirstOrDefault(d => d.FieldKey == fieldKey);

        if (existing is not null)
            throw ConflictException.Duplicate(
                $"A {entityType} custom field with the key '{fieldKey}' already exists.", existing.Id, existing.Label);

        var now = clock.GetUtcNow();
        var definition = new CustomFieldDefinition
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            EntityType = entityType,
            FieldKey = fieldKey,
            Label = label,
            FieldType = fieldType,
            Options = request.Options is { Count: > 0 } ? JsonSerializer.Serialize(request.Options) : null,
            IsRequired = request.IsRequired,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.CustomFieldDefinitions.Add(definition);
        await unitOfWork.SaveChangesAsync(ct);

        return CustomerMapper.ToDto(definition);
    }

    public async Task<CustomFieldDefinitionDto> UpdateAsync(
        Guid id, UpdateCustomFieldRequest request, CancellationToken ct)
    {
        var definition = await lookups.CustomFieldAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No custom field with id {id}.");

        // CF-6: the key and the type never change; only these four may.
        if (request.Label is not null)
        {
            var label = request.Label.Trim();
            if (label.Length == 0) throw new CustomerValidationException("label", "A label is required.");
            definition.Label = label;
        }

        if (request.Options is not null)
        {
            CustomFieldValidator.ValidateDefinition(definition.FieldType, request.Options);
            definition.Options = request.Options.Count > 0 ? JsonSerializer.Serialize(request.Options) : null;
        }

        if (request.IsRequired is { } required) definition.IsRequired = required;
        if (request.SortOrder is { } order) definition.SortOrder = order;

        if (request.IsActive is { } active && active != definition.IsActive)
        {
            // CF-7 again: reactivating must not push the organization past the limit.
            if (active)
            {
                var count = await lookups.ActiveCustomFieldCountAsync(ctx.OrganizationId, definition.EntityType, ct);
                if (count >= _settings.MaxActiveCustomFields)
                    throw new CustomerValidationException("isActive",
                        $"A {definition.EntityType} may have at most {_settings.MaxActiveCustomFields} active custom fields.");
            }

            // CF-5 / AC-11: nothing stored is touched, so the old values reappear on reactivation.
            definition.IsActive = active;
        }

        definition.UpdatedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(ct);

        return CustomerMapper.ToDto(definition);
    }
}
