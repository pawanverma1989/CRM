namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Mapping;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

/// <summary>
/// The two admin-managed value lists behind <c>companies.industry_id</c> and
/// <c>contacts.source_id</c> (§4). On the first read for an organization the agreed defaults are
/// seeded, so a brand-new organization has usable dropdowns without an admin setting them up.
/// </summary>
public interface IPicklistService
{
    Task<IReadOnlyList<PicklistDto>> ListAsync(string listType, bool includeInactive, CancellationToken ct);
    Task<PicklistDto> CreateAsync(CreatePicklistValueRequest request, CancellationToken ct);
    Task<PicklistDto> UpdateAsync(Guid id, UpdatePicklistValueRequest request, CancellationToken ct);
}

public class PicklistService(
    ILookupRepository lookups,
    CustomerDbContext context,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock) : IPicklistService
{
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<PicklistDto>> ListAsync(
        string listType, bool includeInactive, CancellationToken ct)
    {
        var type = RequireListType(listType);
        var values = await lookups.PicklistAsync(ctx.OrganizationId, type, includeInactive, ct);

        if (values.Count == 0)
        {
            // Only an empty list is seeded, so an admin who deactivated everything is respected.
            var anyAtAll = await context.Picklists
                .AnyAsync(p => p.OrganizationId == ctx.OrganizationId && p.ListType == type, ct);

            if (!anyAtAll)
            {
                await SeedAsync(type, ct);
                values = await lookups.PicklistAsync(ctx.OrganizationId, type, includeInactive, ct);
            }
        }

        return [.. values.Select(CustomerMapper.ToDto)];
    }

    public async Task<PicklistDto> CreateAsync(CreatePicklistValueRequest request, CancellationToken ct)
    {
        var type = RequireListType(request.ListType);
        var value = (request.Value ?? string.Empty).Trim();

        if (value.Length == 0) throw new CustomerValidationException("value", "A value is required.");
        if (value.Length > 100) throw new CustomerValidationException("value", "Must be at most 100 characters.");

        var existing = await context.Picklists.FirstOrDefaultAsync(
            p => p.OrganizationId == ctx.OrganizationId && p.ListType == type && p.Value == value, ct);

        if (existing is not null)
            throw ConflictException.Duplicate(
                $"'{value}' is already in this list.", existing.Id, existing.Value);

        var now = clock.GetUtcNow();
        var entry = new Picklist
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            ListType = type,
            Value = value,
            SortOrder = request.SortOrder ?? await NextSortOrderAsync(type, ct),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Picklists.Add(entry);
        await unitOfWork.SaveChangesAsync(ct);

        return CustomerMapper.ToDto(entry);
    }

    public async Task<PicklistDto> UpdateAsync(Guid id, UpdatePicklistValueRequest request, CancellationToken ct)
    {
        var entry = await lookups.PicklistValueAsync(id, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No picklist value with id {id}.");

        if (request.Value is not null)
        {
            var value = request.Value.Trim();
            if (value.Length == 0) throw new CustomerValidationException("value", "A value is required.");
            if (value.Length > 100) throw new CustomerValidationException("value", "Must be at most 100 characters.");
            entry.Value = value;
        }

        if (request.SortOrder is { } order) entry.SortOrder = order;

        // Deactivating hides the value from forms; records already pointing at it keep their label,
        // because the foreign key and the row both stay.
        if (request.IsActive is { } active) entry.IsActive = active;

        entry.UpdatedAt = clock.GetUtcNow();

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == UniqueViolation)
        {
            throw new ConflictException("Another value in this list already uses that name.");
        }

        return CustomerMapper.ToDto(entry);
    }

    private async Task SeedAsync(string listType, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var defaults = PicklistTypes.Defaults(listType);

        for (var i = 0; i < defaults.Length; i++)
            context.Picklists.Add(new Picklist
            {
                Id = Guid.NewGuid(),
                OrganizationId = ctx.OrganizationId,
                ListType = listType,
                Value = defaults[i],
                SortOrder = (i + 1) * 10,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == UniqueViolation)
        {
            // Two first reads raced; the other one won and the list is now seeded either way.
            foreach (var entry in context.ChangeTracker.Entries<Picklist>().ToList())
                entry.State = EntityState.Detached;
        }
    }

    private async Task<int> NextSortOrderAsync(string listType, CancellationToken ct)
    {
        var highest = await context.Picklists
            .Where(p => p.OrganizationId == ctx.OrganizationId && p.ListType == listType)
            .Select(p => (int?)p.SortOrder)
            .MaxAsync(ct);

        return (highest ?? 0) + 10;
    }

    private static string RequireListType(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return PicklistTypes.All.Contains(value)
            ? value
            : throw new CustomerValidationException("type",
                $"Must be one of: {string.Join(", ", PicklistTypes.All)}.");
    }
}
