namespace IdentityApi.Application.Services;
using System.Text.Json;
using IdentityApi.Application.DTOs.Organizations;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;

public class OrganizationService(
    IOrganizationRepository orgRepository,
    IUserRepository userRepository,
    IOutboxRepository outboxRepository,
    IPasswordService passwordService,
    IdentityDbContext context) : IOrganizationService
{
    public async Task<OrganizationDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var org = await orgRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"Organization {id} not found.");
        return ToDto(org);
    }

    public async Task<(OrganizationDto Org, UserDto AdminUser)> CreateWithAdminAsync(CreateOrganizationRequest request, CancellationToken ct = default)
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DefaultCurrency = request.DefaultCurrency,
            Timezone = request.Timezone,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await orgRepository.AddAsync(org, ct);

        var admin = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Email = request.AdminEmail,
            PasswordHash = passwordService.Hash(request.AdminPassword),
            FirstName = request.AdminFirstName,
            LastName = request.AdminLastName,
            Role = "admin",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await userRepository.AddAsync(admin, ct);

        outboxRepository.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            AggregateType = "organization",
            AggregateId = org.Id,
            EventType = "organization.created",
            Payload = JsonSerializer.Serialize(new { org_id = org.Id, name = org.Name, default_currency = org.DefaultCurrency, timezone = org.Timezone }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        outboxRepository.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            AggregateType = "user",
            AggregateId = admin.Id,
            EventType = "user.created",
            Payload = JsonSerializer.Serialize(new { user_id = admin.Id, email = admin.Email, first_name = admin.FirstName, last_name = admin.LastName, role = admin.Role, is_active = admin.IsActive }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync(ct);

        return (ToDto(org), UserToDto(admin));
    }

    public async Task<OrganizationDto> UpdateAsync(Guid id, UpdateOrganizationRequest request, Guid actorId, CancellationToken ct = default)
    {
        var org = await orgRepository.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"Organization {id} not found.");

        if (request.Name is not null) org.Name = request.Name;
        if (request.DefaultCurrency is not null) org.DefaultCurrency = request.DefaultCurrency;
        if (request.Timezone is not null) org.Timezone = request.Timezone;
        org.UpdatedAt = DateTimeOffset.UtcNow;

        orgRepository.Update(org);
        await context.SaveChangesAsync(ct);
        return ToDto(org);
    }

    private static OrganizationDto ToDto(Organization o) => new(o.Id, o.Name, o.DefaultCurrency, o.Timezone, o.IsActive, o.CreatedAt, o.UpdatedAt);
    private static UserDto UserToDto(User u) => new(u.Id, u.OrganizationId, u.TeamId, u.Email, u.FirstName, u.LastName, u.Phone, u.Role, u.IsActive, u.LastLoginAt, u.CreatedAt, u.UpdatedAt);
}
