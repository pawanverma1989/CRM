namespace IdentityApi.Application.Services;
using System.Text.Json;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;

public class UserService(
    IUserRepository userRepository,
    IOutboxRepository outboxRepository,
    IPasswordService passwordService,
    IdentityDbContext context) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(Guid organizationId, CancellationToken ct = default)
    {
        var users = await userRepository.ListByOrganizationAsync(organizationId, ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");
        return ToDto(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var existing = await userRepository.GetByEmailAsync(request.Email, ct);
        if (existing is not null && existing.OrganizationId == organizationId)
            throw new ConflictException($"Email {request.Email} is already in use.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TeamId = request.TeamId,
            Email = request.Email,
            PasswordHash = passwordService.Hash(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Role = request.Role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await userRepository.AddAsync(user, ct);
        outboxRepository.Add(BuildUserEvent(user, "user.created", actorId));
        await context.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");

        if (request.FirstName is not null) user.FirstName = request.FirstName;
        if (request.LastName is not null) user.LastName = request.LastName;
        if (request.Phone is not null) user.Phone = request.Phone;
        if (request.Role is not null) user.Role = request.Role;
        if (request.TeamId.HasValue) user.TeamId = request.TeamId;
        if (request.IsActive.HasValue) user.IsActive = request.IsActive.Value;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        userRepository.Update(user);
        outboxRepository.Add(BuildUserEvent(user, "user.updated", actorId));
        await context.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task DeactivateAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");

        user.IsActive = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);
        outboxRepository.Add(BuildUserEvent(user, "user.deactivated", actorId));
        await context.SaveChangesAsync(ct);
    }

    private OutboxEvent BuildUserEvent(User user, string eventType, Guid actorId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = user.OrganizationId,
        AggregateType = "user",
        AggregateId = user.Id,
        EventType = eventType,
        Payload = JsonSerializer.Serialize(new { user_id = user.Id, email = user.Email, first_name = user.FirstName, last_name = user.LastName, role = user.Role, team_id = user.TeamId, is_active = user.IsActive }),
        OccurredAt = DateTimeOffset.UtcNow
    };

    private static UserDto ToDto(User u) => new(u.Id, u.OrganizationId, u.TeamId, u.Email, u.FirstName, u.LastName, u.Phone, u.Role, u.IsActive, u.LastLoginAt, u.CreatedAt, u.UpdatedAt);
}
