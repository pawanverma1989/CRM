namespace IdentityApi.Application.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

public class AuthService(
    IUserRepository userRepository,
    IOutboxRepository outboxRepository,
    ITokenService tokenService,
    IPasswordService passwordService,
    IdentityDbContext context,
    IOptions<JwtSettings> jwtSettings,
    IOptions<AuthSettings> authSettings) : IAuthService
{
    private readonly JwtSettings _jwt = jwtSettings.Value;
    private readonly AuthSettings _auth = authSettings.Value;

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct);

        if (user is null || !user.IsActive)
        {
            await Task.Delay(200, ct); // constant-time
            throw new UnauthorizedException("Invalid credentials.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTimeOffset.UtcNow)
            throw new UnauthorizedException("Account is locked. Try again later.");

        if (!passwordService.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _auth.MaxFailedLoginAttempts)
                user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(_auth.LockoutDurationMinutes);

            userRepository.Update(user);
            outboxRepository.Add(BuildEvent(user, "user.login_failed", new { user_id = user.Id, email_masked = MaskEmail(user.Email), ip = ipAddress }));
            await context.SaveChangesAsync(ct);
            throw new UnauthorizedException("Invalid credentials.");
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);

        var refreshToken = tokenService.GenerateRefreshToken();
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserSessions.AddAsync(session, ct);

        outboxRepository.Add(BuildEvent(user, "user.logged_in", new { user_id = user.Id, ip = ipAddress }));
        await context.SaveChangesAsync(ct);

        var visibleOwnerIds = await userRepository.GetVisibleOwnerIdsAsync(user.Id, ct);
        var accessToken = tokenService.GenerateAccessToken(user, visibleOwnerIds);
        var userDto = ToDto(user);

        return new LoginResponse(accessToken, refreshToken, _jwt.AccessTokenExpiryMinutes * 60, "Bearer", userDto);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var hash = HashToken(request.RefreshToken);
        var session = await context.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow, ct)
            ?? throw new UnauthorizedException("Invalid or expired refresh token.");

        var user = session.User;
        if (!user.IsActive) throw new UnauthorizedException("Account inactive.");

        session.RevokedAt = DateTimeOffset.UtcNow;
        context.UserSessions.Update(session);

        var newRefreshToken = tokenService.GenerateRefreshToken();
        var newSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(newRefreshToken),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserSessions.AddAsync(newSession, ct);
        await context.SaveChangesAsync(ct);

        var visibleOwnerIds = await userRepository.GetVisibleOwnerIdsAsync(user.Id, ct);
        var accessToken = tokenService.GenerateAccessToken(user, visibleOwnerIds);

        return new LoginResponse(accessToken, newRefreshToken, _jwt.AccessTokenExpiryMinutes * 60, "Bearer", ToDto(user));
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        var hash = HashToken(request.RefreshToken);
        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAt == null, ct);
        if (session is null) return;
        session.RevokedAt = DateTimeOffset.UtcNow;
        context.UserSessions.Update(session);
        await context.SaveChangesAsync(ct);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 1 ? email[0] + "***" + email[at..] : "***";
    }

    private OutboxEvent BuildEvent(User user, string eventType, object payload) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = user.OrganizationId,
        AggregateType = "user",
        AggregateId = user.Id,
        EventType = eventType,
        Payload = JsonSerializer.Serialize(payload),
        OccurredAt = DateTimeOffset.UtcNow
    };

    private static UserDto ToDto(User u) => new(u.Id, u.OrganizationId, u.TeamId, u.Email, u.FirstName, u.LastName, u.Phone, u.Role, u.IsActive, u.LastLoginAt, u.CreatedAt, u.UpdatedAt);
}
