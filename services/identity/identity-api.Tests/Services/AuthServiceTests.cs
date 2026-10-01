namespace IdentityApi.Tests.Services;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Application.Services;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Settings;
using IdentityApi.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IOutboxRepository> _outboxRepo = new();
    private readonly Mock<ITokenService> _tokenService = new();
    private readonly Mock<IPasswordService> _passwordService = new();

    private static readonly JwtSettings JwtSettings = new()
    {
        Issuer = "crm-identity",
        Audience = "crm-services",
        KeyId = "key-v1",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7
    };

    private static readonly AuthSettings AuthSettings = new()
    {
        MaxFailedLoginAttempts = 5,
        LockoutDurationMinutes = 15
    };

    private AuthService CreateService(IdentityDbContext? context = null)
        => new(
            _userRepo.Object,
            _outboxRepo.Object,
            _tokenService.Object,
            _passwordService.Object,
            context ?? DbContextFactory.Create(),
            Options.Create(JwtSettings),
            Options.Create(AuthSettings));

    private static User MakeActiveUser(Guid? id = null, Guid? orgId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OrganizationId = orgId ?? Guid.NewGuid(),
        Email = "user@example.com",
        PasswordHash = "$2a$12$hashhash",
        FirstName = "Alice",
        Role = "sales_rep",
        IsActive = true,
        FailedLoginCount = 0,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ──────────────────────────────── LoginAsync ─────────────────────────────

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsLoginResponse()
    {
        var user = MakeActiveUser();
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email, default)).ReturnsAsync(user);
        _passwordService.Setup(p => p.Verify("secret", user.PasswordHash)).Returns(true);
        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("refresh_token_abc");
        _tokenService.Setup(t => t.GenerateAccessToken(user, It.IsAny<Guid[]?>())).Returns("access_token_xyz");
        _userRepo.Setup(r => r.GetVisibleOwnerIdsAsync(user.Id, default)).ReturnsAsync(Array.Empty<Guid>());

        var svc = CreateService();
        var result = await svc.LoginAsync(new LoginRequest(user.Email, "secret"), "1.2.3.4", "TestAgent");

        result.AccessToken.Should().Be("access_token_xyz");
        result.RefreshToken.Should().Be("refresh_token_abc");
        result.TokenType.Should().Be("Bearer");
        result.User.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsUnauthorizedException()
    {
        _userRepo.Setup(r => r.GetByEmailAsync("unknown@example.com", default)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.LoginAsync(new LoginRequest("unknown@example.com", "secret"), null, null))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsUnauthorizedException()
    {
        var user = MakeActiveUser();
        user.IsActive = false;
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email, default)).ReturnsAsync(user);

        var svc = CreateService();
        await svc.Invoking(s => s.LoginAsync(new LoginRequest(user.Email, "secret"), null, null))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task LoginAsync_LockedAccount_ThrowsUnauthorizedException()
    {
        var user = MakeActiveUser();
        user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10);
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email, default)).ReturnsAsync(user);

        var svc = CreateService();
        await svc.Invoking(s => s.LoginAsync(new LoginRequest(user.Email, "wrong"), null, null))
            .Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*locked*");
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedException()
    {
        var user = MakeActiveUser();
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email, default)).ReturnsAsync(user);
        _passwordService.Setup(p => p.Verify("wrong", user.PasswordHash)).Returns(false);

        var svc = CreateService();
        await svc.Invoking(s => s.LoginAsync(new LoginRequest(user.Email, "wrong"), null, null))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_IncrementsFailedLoginCount()
    {
        var user = MakeActiveUser();
        user.FailedLoginCount = 0;
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email, default)).ReturnsAsync(user);
        _passwordService.Setup(p => p.Verify(It.IsAny<string>(), user.PasswordHash)).Returns(false);

        var svc = CreateService();
        try { await svc.LoginAsync(new LoginRequest(user.Email, "bad"), null, null); } catch { }

        user.FailedLoginCount.Should().Be(1);
        _userRepo.Verify(r => r.Update(user), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_MaxFailedAttempts_LocksAccount()
    {
        var user = MakeActiveUser();
        user.FailedLoginCount = AuthSettings.MaxFailedLoginAttempts - 1;
        _userRepo.Setup(r => r.GetByEmailAsync(user.Email, default)).ReturnsAsync(user);
        _passwordService.Setup(p => p.Verify(It.IsAny<string>(), user.PasswordHash)).Returns(false);

        var svc = CreateService();
        try { await svc.LoginAsync(new LoginRequest(user.Email, "bad"), null, null); } catch { }

        user.LockedUntil.Should().NotBeNull();
        user.LockedUntil.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    // ─────────────────────────────── RefreshAsync ────────────────────────────

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsNewTokenPair()
    {
        var user = MakeActiveUser();
        var context = DbContextFactory.Create();
        var rawToken = "valid_refresh_token";
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
            User = user
        };

        await context.Users.AddAsync(user);
        await context.UserSessions.AddAsync(session);
        await context.SaveChangesAsync();

        _tokenService.Setup(t => t.GenerateRefreshToken()).Returns("new_refresh_token");
        _tokenService.Setup(t => t.GenerateAccessToken(It.IsAny<User>(), It.IsAny<Guid[]?>())).Returns("new_access_token");
        _userRepo.Setup(r => r.GetVisibleOwnerIdsAsync(user.Id, default)).ReturnsAsync(Array.Empty<Guid>());

        var svc = CreateService(context);
        var result = await svc.RefreshAsync(new RefreshRequest(rawToken), "1.2.3.4", "UA");

        result.AccessToken.Should().Be("new_access_token");
        result.RefreshToken.Should().Be("new_refresh_token");
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ThrowsUnauthorizedException()
    {
        var svc = CreateService();
        await svc.Invoking(s => s.RefreshAsync(new RefreshRequest("nonexistent_token"), null, null))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ThrowsUnauthorizedException()
    {
        var user = MakeActiveUser();
        var context = DbContextFactory.Create();
        var rawToken = "expired_token";
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), // expired
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-8),
            User = user
        };

        await context.Users.AddAsync(user);
        await context.UserSessions.AddAsync(session);
        await context.SaveChangesAsync();

        var svc = CreateService(context);
        await svc.Invoking(s => s.RefreshAsync(new RefreshRequest(rawToken), null, null))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task RefreshAsync_RevokedToken_ThrowsUnauthorizedException()
    {
        var user = MakeActiveUser();
        var context = DbContextFactory.Create();
        var rawToken = "revoked_token";
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = DateTimeOffset.UtcNow.AddHours(-1), // revoked
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            User = user
        };

        await context.Users.AddAsync(user);
        await context.UserSessions.AddAsync(session);
        await context.SaveChangesAsync();

        var svc = CreateService(context);
        await svc.Invoking(s => s.RefreshAsync(new RefreshRequest(rawToken), null, null))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    // ─────────────────────────────── LogoutAsync ─────────────────────────────

    [Fact]
    public async Task LogoutAsync_ValidToken_RevokesSession()
    {
        var user = MakeActiveUser();
        var context = DbContextFactory.Create();
        var rawToken = "logout_token";
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
            User = user
        };

        await context.Users.AddAsync(user);
        await context.UserSessions.AddAsync(session);
        await context.SaveChangesAsync();

        // Detach so the service query gets a fresh load
        context.ChangeTracker.Clear();

        var svc = CreateService(context);
        await svc.LogoutAsync(new LogoutRequest(rawToken));

        var updated = await context.UserSessions.FirstAsync(s => s.TokenHash == HashToken(rawToken));
        updated.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_CompletesWithoutError()
    {
        var svc = CreateService();
        // Should not throw — silently ignores missing session
        await svc.Invoking(s => s.LogoutAsync(new LogoutRequest("unknown_token")))
            .Should().NotThrowAsync();
    }
}
