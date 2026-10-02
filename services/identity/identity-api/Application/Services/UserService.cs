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

public class UserService(
    IUserRepository userRepository,
    IOutboxRepository outboxRepository,
    IPasswordService passwordService,
    IEmailService emailService,
    IdentityDbContext context) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(Guid organizationId, string? role, Guid? teamId, string? status, string? search, CancellationToken ct = default)
    {
        var users = await userRepository.ListByOrganizationAsync(organizationId, role, teamId, status, search, ct);
        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"User {id} not found.");
        return ToDto(user);
    }

    public async Task<UserDto> GetMeAsync(Guid userId, Guid organizationId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, organizationId, ct)
            ?? throw new NotFoundException($"User {userId} not found.");
        return ToDto(user);
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
        return ToDto(user);
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
        return ToDto(user);
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

        return ToDto(user);
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
        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userToken.UsedAt = DateTimeOffset.UtcNow;

        userRepository.Update(user);
        context.UserTokens.Update(userToken);

        outboxRepository.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = user.OrganizationId,
            AggregateType = "user",
            AggregateId = user.Id,
            EventType = "user.created",
            Payload = JsonSerializer.Serialize(new { user_id = user.Id, email = user.Email, first_name = user.FirstName, last_name = user.LastName, role = user.Role }),
            OccurredAt = DateTimeOffset.UtcNow
        });

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

    private static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static UserDto ToDto(User u) => new(u.Id, u.OrganizationId, u.TeamId, u.Email, u.FirstName, u.LastName, u.Phone, u.Role, u.Status, u.LastLoginAt, u.CreatedAt, u.UpdatedAt);
}
