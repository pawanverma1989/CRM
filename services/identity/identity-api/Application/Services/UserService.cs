namespace IdentityApi.Application.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Helpers;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class UserService(
    IUserRepository userRepository,
    ITeamRepository teamRepository,
    IOutboxRepository outboxRepository,
    IPasswordService passwordService,
    IEmailService emailService,
    IdentityDbContext context,
    ILogger<UserService> logger) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(Guid organizationId, string? role, Guid? teamId, string? status, string? search, CancellationToken ct = default)
    {
        var users = await userRepository.ListByOrganizationAsync(organizationId, role, teamId, status, search, ct);
        return users.Select(UserMappings.ToDto).ToList();
    }

    public async Task<UserDto> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");
        return user.ToDto();
    }

    public async Task<UserDto> GetMeAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, organizationId, ct)
            ?? throw new NotFoundException($"User {userId} not found.");
        return user.ToDto();
    }

    public async Task<UserDto> UpdateMeAsync(Guid userId, UpdateMeRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException($"User {userId} not found.");

        if (request.FirstName is not null) user.FirstName = request.FirstName;
        if (request.LastName is not null) user.LastName = request.LastName;
        if (request.Phone is not null) user.Phone = request.Phone;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        userRepository.Update(user);
        outboxRepository.Add(BuildUserEvent(user, "user.updated", user.Id));
        await context.SaveChangesAsync(ct);
        return user.ToDto();
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");

        if (request.Role is not null && request.Role != user.Role)
        {
            if (user.Role == "admin")
            {
                var activeAdmins = await userRepository.CountActiveAdminsAsync(organizationId, ct);
                if (activeAdmins <= 1)
                    throw new ConflictException("Cannot change the role of the last active admin.");
            }
            user.Role = request.Role;
        }
        if (request.TeamId.HasValue) user.TeamId = request.TeamId;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        userRepository.Update(user);
        outboxRepository.Add(BuildUserEvent(user, "user.updated", actorId));
        await context.SaveChangesAsync(ct);
        return user.ToDto();
    }

    /// <summary>
    /// Admin creates a user directly with an initial password. The user is active immediately and is
    /// flagged with MustChangePassword so the UI suggests a change on every login until they change it.
    /// No email is sent. organizationId/actorId always come from the caller's JWT.
    /// </summary>
    public async Task<UserDto> CreateAsync(CreateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        // users.email is globally unique (login looks users up by email alone), so any match is a conflict.
        var existing = await userRepository.GetByEmailAsync(request.Email, ct);
        if (existing is not null)
            throw new ConflictException($"Email {request.Email} is already in use.");

        if (request.TeamId.HasValue)
        {
            _ = await teamRepository.GetByIdAsync(request.TeamId.Value, organizationId, ct)
                ?? throw new NotFoundException($"Team {request.TeamId} not found in this organization.");
        }

        // Defence in depth; the validator already rejects these with 400. Never echo the password.
        if (request.Password.Length < 10 || CommonPasswords.Contains(request.Password))
            throw new ConflictException("Password does not meet requirements.");

        var now = DateTimeOffset.UtcNow;
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
            Status = "active",
            MustChangePassword = true,
            EmailVerifiedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };
        await userRepository.AddAsync(user, ct);

        // Same SaveChangesAsync => same transaction as the user row. Never put the password or its hash here.
        outboxRepository.Add(BuildUserCreatedEvent(user, createdBy: actorId, creationMethod: "admin_set_password"));
        await context.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} created with admin-set password by {ActorId} in organization {OrganizationId}",
            user.Id, actorId, organizationId);

        return user.ToDto();
    }

    public async Task<UserDto> InviteAsync(InviteUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var existing = await userRepository.GetByEmailAsync(request.Email, ct);
        if (existing is not null)
            throw new ConflictException($"Email {request.Email} is already in use.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            TeamId = request.TeamId,
            Email = request.Email,
            PasswordHash = string.Empty,
            FirstName = string.Empty,
            Role = request.Role,
            Status = "invited",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await userRepository.AddAsync(user, ct);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            Purpose = "invitation",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserTokens.AddAsync(userToken, ct);

        outboxRepository.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AggregateType = "user",
            AggregateId = user.Id,
            EventType = "user.invited",
            Payload = JsonSerializer.Serialize(new { user_id = user.Id, email = user.Email, role = user.Role, team_id = user.TeamId, invited_by = actorId }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync(ct);
        await emailService.SendInvitationAsync(user.Email, rawToken, ct);

        return user.ToDto();
    }

    public async Task ResendInvitationAsync(Guid invitationId, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(invitationId, organizationId, ct)
            ?? throw new NotFoundException($"User {invitationId} not found.");

        if (user.Status != "invited")
            throw new ConflictException("User is not in 'invited' status.");

        var oldTokens = await context.UserTokens
            .Where(t => t.UserId == user.Id && t.Purpose == "invitation" && t.UsedAt == null)
            .ToListAsync(ct);
        foreach (var t in oldTokens)
            t.UsedAt = DateTimeOffset.UtcNow;

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            Purpose = "invitation",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserTokens.AddAsync(userToken, ct);

        outboxRepository.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AggregateType = "user",
            AggregateId = user.Id,
            EventType = "user.invited",
            Payload = JsonSerializer.Serialize(new { user_id = user.Id, email = user.Email, role = user.Role, team_id = user.TeamId, invited_by = actorId }),
            OccurredAt = DateTimeOffset.UtcNow
        });

        await context.SaveChangesAsync(ct);
        await emailService.SendInvitationAsync(user.Email, rawToken, ct);
    }

    public async Task CancelInvitationAsync(Guid invitationId, Guid organizationId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(invitationId, organizationId, ct)
            ?? throw new NotFoundException($"User {invitationId} not found.");

        if (user.Status != "invited")
            throw new ConflictException("User is not in 'invited' status.");

        var oldTokens = await context.UserTokens
            .Where(t => t.UserId == user.Id && t.Purpose == "invitation" && t.UsedAt == null)
            .ToListAsync(ct);
        foreach (var t in oldTokens)
            t.UsedAt = DateTimeOffset.UtcNow;

        user.Status = "deactivated";
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);
        await context.SaveChangesAsync(ct);
    }

    public async Task AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken ct = default)
    {
        var hash = HashToken(request.Token);
        var userToken = await context.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == "invitation", ct)
            ?? throw new UnauthorizedException("Invalid or expired invitation token.");

        if (userToken.UsedAt is not null || userToken.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedException("Invalid or expired invitation token.");

        if (request.Password.Length < 10 || CommonPasswords.Contains(request.Password))
            throw new ConflictException("Password does not meet requirements.");

        var user = userToken.User;
        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.PasswordHash = passwordService.Hash(request.Password);
        user.Status = "active";
        user.MustChangePassword = false; // the user chose this password themselves
        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userToken.UsedAt = DateTimeOffset.UtcNow;

        userRepository.Update(user);
        context.UserTokens.Update(userToken);

        outboxRepository.Add(BuildUserCreatedEvent(user, createdBy: null, creationMethod: "invitation"));

        await context.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");

        if (user.Role == "admin")
        {
            var activeAdmins = await userRepository.CountActiveAdminsAsync(organizationId, ct);
            if (activeAdmins <= 1)
                throw new ConflictException("Cannot deactivate the last active admin.");
        }

        user.Status = "deactivated";
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);

        var sessions = await context.UserSessions
            .Where(s => s.UserId == id && s.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions)
            s.RevokedAt = DateTimeOffset.UtcNow;

        outboxRepository.Add(BuildUserEvent(user, "user.deactivated", actorId));
        await context.SaveChangesAsync(ct);
    }

    public async Task ReactivateAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");

        user.Status = "active";
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);
        outboxRepository.Add(BuildUserEvent(user, "user.reactivated", actorId));
        await context.SaveChangesAsync(ct);
    }

    public async Task AdminLogoutAllAsync(Guid targetUserId, Guid organizationId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(targetUserId, organizationId, ct)
            ?? throw new NotFoundException($"User {targetUserId} not found.");

        var sessions = await context.UserSessions
            .Where(s => s.UserId == targetUserId && s.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions)
            s.RevokedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(ct);
    }

    public async Task InitiateEmailChangeAsync(Guid userId, string newEmail, Guid organizationId, CancellationToken ct = default)
    {
        var existing = await userRepository.GetByEmailAsync(newEmail, ct);
        if (existing is not null)
            throw new ConflictException($"Email {newEmail} is already in use.");

        var user = await userRepository.GetByIdAsync(userId, organizationId, ct)
            ?? throw new NotFoundException($"User {userId} not found.");

        user.PendingEmail = newEmail;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(rawToken),
            Purpose = "email_verification",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserTokens.AddAsync(userToken, ct);
        await context.SaveChangesAsync(ct);

        await emailService.SendEmailVerificationAsync(newEmail, rawToken, ct);
    }

    public async Task ConfirmEmailChangeAsync(string token, CancellationToken ct = default)
    {
        var hash = HashToken(token);
        var userToken = await context.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == "email_verification", ct)
            ?? throw new UnauthorizedException("Invalid or expired email verification token.");

        if (userToken.UsedAt is not null || userToken.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedException("Invalid or expired email verification token.");

        var user = userToken.User;
        if (user.PendingEmail is null)
            throw new ConflictException("No pending email change found.");

        user.Email = user.PendingEmail;
        user.PendingEmail = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userToken.UsedAt = DateTimeOffset.UtcNow;

        userRepository.Update(user);
        context.UserTokens.Update(userToken);

        outboxRepository.Add(BuildUserEvent(user, "user.updated", user.Id));
        await context.SaveChangesAsync(ct);
    }

    private OutboxEvent BuildUserEvent(User user, string eventType, Guid actorId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = user.OrganizationId,
        AggregateType = "user",
        AggregateId = user.Id,
        EventType = eventType,
        Payload = JsonSerializer.Serialize(new { user_id = user.Id, email = user.Email, first_name = user.FirstName, last_name = user.LastName, role = user.Role, team_id = user.TeamId, status = user.Status, actor_id = actorId }),
        OccurredAt = DateTimeOffset.UtcNow
    };

    // user.created payload shared by both creation paths (invitation accepted / admin-set password).
    private static OutboxEvent BuildUserCreatedEvent(User user, Guid? createdBy, string creationMethod) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = user.OrganizationId,
        AggregateType = "user",
        AggregateId = user.Id,
        EventType = "user.created",
        Payload = JsonSerializer.Serialize(new
        {
            user_id = user.Id,
            email = user.Email,
            first_name = user.FirstName,
            last_name = user.LastName,
            role = user.Role,
            team_id = user.TeamId,
            created_by = createdBy,
            creation_method = creationMethod
        }),
        OccurredAt = DateTimeOffset.UtcNow
    };

    private static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

}
