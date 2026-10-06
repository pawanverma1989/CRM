namespace IdentityApi.Tests.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Application.Services;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Infrastructure.Repositories;
using IdentityApi.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public class UserServiceTests
{
    private const string StrongPassword = "Corr3ct-Horse-Battery";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITeamRepository> _teamRepo = new();
    private readonly Mock<IOutboxRepository> _outboxRepo = new();
    private readonly Mock<IPasswordService> _passwordService = new();
    private readonly Mock<IEmailService> _emailService = new();

    private UserService CreateService(IdentityDbContext? context = null)
        => new(_userRepo.Object, _teamRepo.Object, _outboxRepo.Object, _passwordService.Object,
               _emailService.Object, context ?? DbContextFactory.Create(), NullLogger<UserService>.Instance);

    private static User MakeUser(Guid? id = null, Guid? orgId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OrganizationId = orgId ?? Guid.NewGuid(),
        Email = "user@example.com",
        PasswordHash = "hash",
        FirstName = "Jane",
        LastName = "Doe",
        Role = "sales_rep",
        Status = "active",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    // ─────────────────────────────── Queries ─────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsAllUsersForOrg()
    {
        var orgId = Guid.NewGuid();
        var users = new List<User> { MakeUser(orgId: orgId), MakeUser(orgId: orgId) };
        _userRepo.Setup(r => r.ListByOrganizationAsync(orgId, null, null, null, null, default)).ReturnsAsync(users);

        var result = await CreateService().ListAsync(orgId, null, null, null, null);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingUser_ReturnsUserDto()
    {
        var user = MakeUser();
        user.MustChangePassword = true;
        _userRepo.Setup(r => r.GetByIdAsync(user.Id, user.OrganizationId, default)).ReturnsAsync(user);

        var result = await CreateService().GetByIdAsync(user.Id, user.OrganizationId);

        result.Id.Should().Be(user.Id);
        result.Email.Should().Be(user.Email);
        result.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((User?)null);

        await CreateService().Invoking(s => s.GetByIdAsync(id, orgId))
            .Should().ThrowAsync<NotFoundException>();
    }

    // ─────────────────────────────── CreateAsync ─────────────────────────────

    [Fact]
    public async Task CreateAsync_NewEmail_CreatesActiveUserFlaggedToChangePassword()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var request = new CreateUserRequest("new@example.com", StrongPassword, "Bob", "Smith", "+911234567890", "sales_rep", null);

        _userRepo.Setup(r => r.GetByEmailAsync(request.Email, default)).ReturnsAsync((User?)null);
        _passwordService.Setup(p => p.Hash(StrongPassword)).Returns("bcrypt_hash");
        User? added = null;
        _userRepo.Setup(r => r.AddAsync(It.IsAny<User>(), default))
            .Callback<User, CancellationToken>((u, _) => added = u)
            .Returns(Task.CompletedTask);

        var result = await CreateService().CreateAsync(request, orgId, actorId);

        added.Should().NotBeNull();
        added!.OrganizationId.Should().Be(orgId);
        added.Status.Should().Be("active");
        added.MustChangePassword.Should().BeTrue();
        added.PasswordHash.Should().Be("bcrypt_hash");
        added.EmailVerifiedAt.Should().BeNull();
        added.FirstName.Should().Be("Bob");
        added.LastName.Should().Be("Smith");
        added.Phone.Should().Be("+911234567890");
        added.Role.Should().Be("sales_rep");

        result.Email.Should().Be(request.Email);
        result.OrganizationId.Should().Be(orgId);
        result.Status.Should().Be("active");
        result.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_NewEmail_EmitsUserCreatedEventWithoutPassword()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var request = new CreateUserRequest("new@example.com", StrongPassword, "Bob", null, null, "manager", teamId);

        _userRepo.Setup(r => r.GetByEmailAsync(request.Email, default)).ReturnsAsync((User?)null);
        _teamRepo.Setup(r => r.GetByIdAsync(teamId, orgId, default))
            .ReturnsAsync(new Team { Id = teamId, OrganizationId = orgId, Name = "North" });
        _passwordService.Setup(p => p.Hash(StrongPassword)).Returns("bcrypt_hash");
        OutboxEvent? evt = null;
        _outboxRepo.Setup(r => r.Add(It.IsAny<OutboxEvent>())).Callback<OutboxEvent>(e => evt = e);

        var result = await CreateService().CreateAsync(request, orgId, actorId);

        evt.Should().NotBeNull();
        evt!.EventType.Should().Be("user.created");
        evt.AggregateType.Should().Be("user");
        evt.AggregateId.Should().Be(result.Id);
        evt.OrganizationId.Should().Be(orgId);
        evt.Payload.Should().NotContain(StrongPassword).And.NotContain("bcrypt_hash");

        using var doc = JsonDocument.Parse(evt.Payload);
        var p = doc.RootElement;
        p.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo(
            "user_id", "email", "first_name", "last_name", "role", "team_id", "created_by", "creation_method");
        p.GetProperty("user_id").GetGuid().Should().Be(result.Id);
        p.GetProperty("email").GetString().Should().Be("new@example.com");
        p.GetProperty("first_name").GetString().Should().Be("Bob");
        p.GetProperty("last_name").ValueKind.Should().Be(JsonValueKind.Null);
        p.GetProperty("role").GetString().Should().Be("manager");
        p.GetProperty("team_id").GetGuid().Should().Be(teamId);
        p.GetProperty("created_by").GetGuid().Should().Be(actorId);
        p.GetProperty("creation_method").GetString().Should().Be("admin_set_password");
    }

    [Fact]
    public async Task CreateAsync_NewEmail_DoesNotSendAnyEmail()
    {
        var request = new CreateUserRequest("new@example.com", StrongPassword, "Bob", null, null, "sales_rep", null);
        _userRepo.Setup(r => r.GetByEmailAsync(request.Email, default)).ReturnsAsync((User?)null);
        _passwordService.Setup(p => p.Hash(It.IsAny<string>())).Returns("bcrypt_hash");

        await CreateService().CreateAsync(request, Guid.NewGuid(), Guid.NewGuid());

        _emailService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_NewEmail_PersistsUserAndOutboxEventInSameSaveChanges()
    {
        // Real repositories over one in-memory context: both rows must be written by the single SaveChangesAsync.
        var context = DbContextFactory.Create();
        var svc = new UserService(new UserRepository(context), new TeamRepository(context), new OutboxRepository(context),
            _passwordService.Object, _emailService.Object, context, NullLogger<UserService>.Instance);
        _passwordService.Setup(p => p.Hash(It.IsAny<string>())).Returns("bcrypt_hash");
        var orgId = Guid.NewGuid();

        var result = await svc.CreateAsync(
            new CreateUserRequest("new@example.com", StrongPassword, "Bob", null, null, "sales_rep", null), orgId, Guid.NewGuid());

        context.ChangeTracker.Clear();
        var saved = await context.Users.SingleAsync(u => u.Id == result.Id);
        saved.MustChangePassword.Should().BeTrue();
        saved.Status.Should().Be("active");
        (await context.OutboxEvents.CountAsync(e => e.AggregateId == result.Id && e.EventType == "user.created")).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmailSameOrg_ThrowsConflictException()
    {
        var orgId = Guid.NewGuid();
        var existingUser = MakeUser(orgId: orgId);
        existingUser.Email = "dup@example.com";
        _userRepo.Setup(r => r.GetByEmailAsync("dup@example.com", default)).ReturnsAsync(existingUser);

        var request = new CreateUserRequest("dup@example.com", StrongPassword, "X", null, null, "sales_rep", null);

        await CreateService().Invoking(s => s.CreateAsync(request, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<ConflictException>();
        _userRepo.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxRepo.Verify(r => r.Add(It.IsAny<OutboxEvent>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmailDifferentOrg_ThrowsConflictException()
    {
        // users.email has a GLOBAL unique index (V1: email CITEXT NOT NULL UNIQUE), because login looks a
        // user up by email alone. An address used in any other organization can therefore never be inserted.
        var orgId = Guid.NewGuid();
        var existingUser = MakeUser(orgId: Guid.NewGuid());
        existingUser.Email = "shared@example.com";
        _userRepo.Setup(r => r.GetByEmailAsync("shared@example.com", default)).ReturnsAsync(existingUser);

        var request = new CreateUserRequest("shared@example.com", StrongPassword, "X", null, null, "sales_rep", null);

        await CreateService().Invoking(s => s.CreateAsync(request, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<ConflictException>();
        _userRepo.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_TeamFromAnotherOrg_ThrowsNotFoundException()
    {
        var orgId = Guid.NewGuid();
        var foreignTeamId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), default)).ReturnsAsync((User?)null);
        // Team lookup is org-scoped, so a team that belongs to another org is not found.
        _teamRepo.Setup(r => r.GetByIdAsync(foreignTeamId, orgId, default)).ReturnsAsync((Team?)null);

        var request = new CreateUserRequest("new@example.com", StrongPassword, "Bob", null, null, "sales_rep", foreignTeamId);

        await CreateService().Invoking(s => s.CreateAsync(request, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
        _userRepo.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _outboxRepo.Verify(r => r.Add(It.IsAny<OutboxEvent>()), Times.Never);
    }

    [Theory]
    [InlineData("short1!")]
    [InlineData("password1234")]
    public async Task CreateAsync_WeakPassword_ThrowsConflictException(string weak)
    {
        // Defence in depth: the validator returns 400 first, but the service never stores a weak password.
        _userRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), default)).ReturnsAsync((User?)null);
        var request = new CreateUserRequest("new@example.com", weak, "Bob", null, null, "sales_rep", null);

        var ex = await CreateService().Invoking(s => s.CreateAsync(request, Guid.NewGuid(), Guid.NewGuid()))
            .Should().ThrowAsync<ConflictException>();
        ex.Which.Message.Should().NotContain(weak);
        _passwordService.Verify(p => p.Hash(It.IsAny<string>()), Times.Never);
    }

    // ─────────────────────────── AcceptInvitationAsync ───────────────────────

    [Fact]
    public async Task AcceptInvitationAsync_ValidToken_ActivatesUserAndEmitsConsistentUserCreated()
    {
        var context = DbContextFactory.Create();
        var teamId = Guid.NewGuid();
        var user = MakeUser();
        user.Status = "invited";
        user.PasswordHash = string.Empty;
        user.FirstName = string.Empty;
        user.TeamId = teamId;
        user.MustChangePassword = true; // never true for invited users in practice; proves the flag is cleared
        const string rawToken = "invite-token";
        context.Users.Add(user);
        context.UserTokens.Add(new UserToken
        {
            Id = Guid.NewGuid(), UserId = user.Id, TokenHash = HashToken(rawToken), Purpose = "invitation",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), CreatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();

        _passwordService.Setup(p => p.Hash(StrongPassword)).Returns("bcrypt_hash");
        OutboxEvent? evt = null;
        _outboxRepo.Setup(r => r.Add(It.IsAny<OutboxEvent>())).Callback<OutboxEvent>(e => evt = e);

        await CreateService(context).AcceptInvitationAsync(new AcceptInvitationRequest(rawToken, "Jane", "Doe", StrongPassword));

        user.Status.Should().Be("active");
        user.MustChangePassword.Should().BeFalse();
        evt!.EventType.Should().Be("user.created");
        evt.Payload.Should().NotContain(StrongPassword).And.NotContain("bcrypt_hash");
        using var doc = JsonDocument.Parse(evt.Payload);
        doc.RootElement.GetProperty("team_id").GetGuid().Should().Be(teamId);
        doc.RootElement.GetProperty("created_by").ValueKind.Should().Be(JsonValueKind.Null);
        doc.RootElement.GetProperty("creation_method").GetString().Should().Be("invitation");
    }

    // ─────────────────────────────── UpdateAsync ─────────────────────────────

    [Fact]
    public async Task UpdateAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((User?)null);

        await CreateService().Invoking(s => s.UpdateAsync(id, new UpdateUserRequest(null, null, null, null, null, null), orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    // ─────────────────────────────── DeactivateAsync ─────────────────────────

    [Fact]
    public async Task DeactivateAsync_ExistingUser_SetsStatusDeactivated()
    {
        var user = MakeUser();
        _userRepo.Setup(r => r.GetByIdAsync(user.Id, user.OrganizationId, default)).ReturnsAsync(user);

        await CreateService().DeactivateAsync(user.Id, user.OrganizationId, Guid.NewGuid());

        user.Status.Should().Be("deactivated");
        _outboxRepo.Verify(r => r.Add(It.Is<OutboxEvent>(e => e.EventType == "user.deactivated")), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((User?)null);

        await CreateService().Invoking(s => s.DeactivateAsync(id, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }
}
