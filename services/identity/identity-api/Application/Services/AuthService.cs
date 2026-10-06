namespace IdentityApi.Application.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Helpers;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class AuthService(
    IUserRepository userRepository,
    IOutboxRepository outboxRepository,
    ITokenService tokenService,
    IPasswordService passwordService,
    IEmailService emailService,
    IdentityDbContext context,
    IOptions<JwtSettings> jwtSettings,
    IOptions<AuthSettings> authSettings,
    ILogger<AuthService> logger) : IAuthService
{
    private readonly JwtSettings _jwt = jwtSettings.Value;
    private readonly AuthSettings _auth = authSettings.Value;

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct);

        if (user is null || user.Status != "active")
        {
            await Task.Delay(200, ct);
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
            LastUsedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserSessions.AddAsync(session, ct);

        outboxRepository.Add(BuildEvent(user, "user.logged_in", new { user_id = user.Id, ip = ipAddress }));
        await context.SaveChangesAsync(ct);

        var visibleOwnerIds = await userRepository.GetVisibleOwnerIdsAsync(user.Id, ct);
        var accessToken = tokenService.GenerateAccessToken(user, visibleOwnerIds);

        return new LoginResponse(accessToken, refreshToken, _jwt.AccessTokenExpiryMinutes * 60, "Bearer", user.ToDto());
    }

    public async Task<LoginResponse> RefreshAsync(RefreshRequest request, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var hash = HashToken(request.RefreshToken);
        var session = await context.UserSessions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedException("Invalid or expired refresh token.");

        if (session.RevokedAt is not null)
        {
            // Token reuse detected — revoke all sessions for this user
            var allSessions = await context.UserSessions
                .Where(s => s.UserId == session.UserId && s.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var s in allSessions)
                s.RevokedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(ct);
            logger.LogWarning("Token reuse detected for user {UserId}. All sessions revoked.", session.UserId);
            throw new UnauthorizedException("Token reuse detected.");
        }

        var user = session.User;
        if (user.Status != "active") throw new UnauthorizedException("Account inactive.");

        var newRefreshToken = tokenService.GenerateRefreshToken();
        var newSession = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(newRefreshToken),
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays),
            LastUsedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserSessions.AddAsync(newSession, ct);

        session.RevokedAt = DateTimeOffset.UtcNow;
        session.ReplacedById = newSession.Id;
        context.UserSessions.Update(session);

        await context.SaveChangesAsync(ct);

        var visibleOwnerIds = await userRepository.GetVisibleOwnerIdsAsync(user.Id, ct);
        var accessToken = tokenService.GenerateAccessToken(user, visibleOwnerIds);

        return new LoginResponse(accessToken, newRefreshToken, _jwt.AccessTokenExpiryMinutes * 60, "Bearer", user.ToDto());
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

    public async Task LogoutAllAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions)
            s.RevokedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(ct);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByEmailAsync(request.Email, ct);
        if (user is null) return; // silent — don't reveal existence

        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var recentCount = await context.UserTokens
            .CountAsync(t => t.UserId == user.Id && t.Purpose == "password_reset" && t.CreatedAt >= oneHourAgo, ct);

        if (recentCount >= 3)
        {
            logger.LogWarning("Rate limit hit for password reset for user {UserId}", user.Id);
            return;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var userToken = new UserToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            Purpose = "password_reset",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await context.UserTokens.AddAsync(userToken, ct);
        await context.SaveChangesAsync(ct);

        await emailService.SendPasswordResetAsync(user.Email, rawToken, ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var hash = HashToken(request.Token);
        var userToken = await context.UserTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == "password_reset", ct);

        if (userToken is null || userToken.UsedAt is not null || userToken.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedException("Invalid or expired password reset token.");

        if (request.NewPassword.Length < 10 || CommonPasswords.Contains(request.NewPassword))
            throw new ConflictException("Password does not meet requirements.");

        var user = userToken.User;
        user.PasswordHash = passwordService.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userToken.UsedAt = DateTimeOffset.UtcNow;

        userRepository.Update(user);
        context.UserTokens.Update(userToken);

        var sessions = await context.UserSessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions)
            s.RevokedAt = DateTimeOffset.UtcNow;

        outboxRepository.Add(BuildEvent(user, "user.password_changed", new { user_id = user.Id }));
        await context.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException($"User {userId} not found.");

        if (!passwordService.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedException("Current password is incorrect.");

        // CurrentPassword has just been verified against the stored hash, so plain equality is enough here.
        if (string.Equals(request.NewPassword, request.CurrentPassword, StringComparison.Ordinal))
            throw new ConflictException("New password must be different from the current password.");

        if (request.NewPassword.Length < 10 || CommonPasswords.Contains(request.NewPassword))
            throw new ConflictException("Password does not meet requirements.");

        user.PasswordHash = passwordService.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        userRepository.Update(user);

        var sessions = await context.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions)
            s.RevokedAt = DateTimeOffset.UtcNow;

        outboxRepository.Add(BuildEvent(user, "user.password_changed", new { user_id = user.Id }));
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var sessions = await context.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        return sessions.Select(s => new SessionDto(s.Id, s.IpAddress, s.UserAgent, s.CreatedAt, s.ExpiresAt, s.LastUsedAt)).ToList();
    }

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await context.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new NotFoundException($"Session {sessionId} not found.");

        if (session.RevokedAt is null)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            context.UserSessions.Update(session);
            await context.SaveChangesAsync(ct);
        }
    }

    public async Task<ServiceTokenResponse> IssueServiceTokenAsync(ServiceTokenRequest request, CancellationToken ct = default)
    {
        if (request.GrantType != "client_credentials")
            throw new UnauthorizedException("Unsupported grant_type. Use 'client_credentials'.");

        var client = await context.ServiceClients
            .FirstOrDefaultAsync(c => c.ClientId == request.ClientId && c.IsActive, ct)
            ?? throw new UnauthorizedException("Invalid client credentials.");

        if (!passwordService.Verify(request.ClientSecret, client.SecretHash))
            throw new UnauthorizedException("Invalid client credentials.");

        var accessToken = tokenService.GenerateServiceToken(client.ClientId, client.ServiceName, client.OrganizationId);
        return new ServiceTokenResponse(accessToken, 3600, "Bearer");
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

}
