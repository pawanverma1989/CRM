namespace IdentityApi.Tests.Services;
using FluentAssertions;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Application.Services;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Tests.Helpers;
using Moq;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IOutboxRepository> _outboxRepo = new();
    private readonly Mock<IPasswordService> _passwordService = new();

    private UserService CreateService()
        => new(_userRepo.Object, _outboxRepo.Object, _passwordService.Object, DbContextFactory.Create());

    private static User MakeUser(Guid? id = null, Guid? orgId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OrganizationId = orgId ?? Guid.NewGuid(),
        Email = "user@example.com",
        PasswordHash = "hash",
        FirstName = "Jane",
        LastName = "Doe",
        Role = "sales_rep",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ListAsync_ReturnsAllActiveUsers()
    {
        var orgId = Guid.NewGuid();
        var users = new List<User> { MakeUser(orgId: orgId), MakeUser(orgId: orgId) };
        _userRepo.Setup(r => r.ListByOrganizationAsync(orgId, default)).ReturnsAsync(users);

        var svc = CreateService();
        var result = await svc.ListAsync(orgId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingUser_ReturnsUserDto()
    {
        var user = MakeUser();
        _userRepo.Setup(r => r.GetByIdAsync(user.Id, user.OrganizationId, default)).ReturnsAsync(user);

        var svc = CreateService();
        var result = await svc.GetByIdAsync(user.Id, user.OrganizationId);

        result.Id.Should().Be(user.Id);
        result.Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.GetByIdAsync(id, orgId))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_NewEmail_CreatesAndReturnsUserDto()
    {
        var orgId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var request = new CreateUserRequest("new@example.com", "P@ss1!", "Bob", null, null, "sales_rep", null);

        _userRepo.Setup(r => r.GetByEmailAsync(request.Email, default)).ReturnsAsync((User?)null);
        _passwordService.Setup(p => p.Hash(request.Password)).Returns("bcrypt_hash");

        var svc = CreateService();
        var result = await svc.CreateAsync(request, orgId, actorId);

        result.Email.Should().Be(request.Email);
        result.OrganizationId.Should().Be(orgId);
        _userRepo.Verify(r => r.AddAsync(It.IsAny<User>(), default), Times.Once);
        _outboxRepo.Verify(r => r.Add(It.Is<IdentityApi.Domain.Entities.OutboxEvent>(e => e.EventType == "user.created")), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmailSameOrg_ThrowsConflictException()
    {
        var orgId = Guid.NewGuid();
        var existingUser = MakeUser(orgId: orgId);
        existingUser.Email = "dup@example.com";

        _userRepo.Setup(r => r.GetByEmailAsync("dup@example.com", default)).ReturnsAsync(existingUser);

        var svc = CreateService();
        var request = new CreateUserRequest("dup@example.com", "P@ss!", "X", null, null, "sales_rep", null);

        await svc.Invoking(s => s.CreateAsync(request, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmailDifferentOrg_Succeeds()
    {
        var orgId = Guid.NewGuid();
        var existingUser = MakeUser(orgId: Guid.NewGuid()); // different org
        existingUser.Email = "shared@example.com";

        _userRepo.Setup(r => r.GetByEmailAsync("shared@example.com", default)).ReturnsAsync(existingUser);
        _passwordService.Setup(p => p.Hash(It.IsAny<string>())).Returns("hash");

        var svc = CreateService();
        var request = new CreateUserRequest("shared@example.com", "P@ss!", "X", null, null, "sales_rep", null);

        var result = await svc.CreateAsync(request, orgId, Guid.NewGuid());

        result.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public async Task UpdateAsync_ExistingUser_UpdatesFields()
    {
        var user = MakeUser();
        _userRepo.Setup(r => r.GetByIdAsync(user.Id, user.OrganizationId, default)).ReturnsAsync(user);

        var svc = CreateService();
        var request = new UpdateUserRequest("NewFirst", "NewLast", null, null, null, null);
        var result = await svc.UpdateAsync(user.Id, request, user.OrganizationId, Guid.NewGuid());

        result.FirstName.Should().Be("NewFirst");
        result.LastName.Should().Be("NewLast");
        _userRepo.Verify(r => r.Update(user), Times.Once);
        _outboxRepo.Verify(r => r.Add(It.Is<IdentityApi.Domain.Entities.OutboxEvent>(e => e.EventType == "user.updated")), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.UpdateAsync(id, new UpdateUserRequest(null, null, null, null, null, null), orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeactivateAsync_ExistingUser_SetsIsActiveFalse()
    {
        var user = MakeUser();
        _userRepo.Setup(r => r.GetByIdAsync(user.Id, user.OrganizationId, default)).ReturnsAsync(user);

        var svc = CreateService();
        await svc.DeactivateAsync(user.Id, user.OrganizationId, Guid.NewGuid());

        user.IsActive.Should().BeFalse();
        _outboxRepo.Verify(r => r.Add(It.Is<IdentityApi.Domain.Entities.OutboxEvent>(e => e.EventType == "user.deactivated")), Times.Once);
    }

    [Fact]
    public async Task DeactivateAsync_NonExistingUser_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((User?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.DeactivateAsync(id, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }
}
