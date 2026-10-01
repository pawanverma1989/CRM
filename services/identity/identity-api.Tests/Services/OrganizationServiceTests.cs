namespace IdentityApi.Tests.Services;
using FluentAssertions;
using IdentityApi.Application.DTOs.Organizations;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Application.Services;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Tests.Helpers;
using Moq;

public class OrganizationServiceTests
{
    private readonly Mock<IOrganizationRepository> _orgRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IOutboxRepository> _outboxRepo = new();
    private readonly Mock<IPasswordService> _passwordService = new();

    private OrganizationService CreateService()
        => new(_orgRepo.Object, _userRepo.Object, _outboxRepo.Object, _passwordService.Object, DbContextFactory.Create());

    private static Organization MakeOrg(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "Acme Corp",
        DefaultCurrency = "INR",
        Timezone = "Asia/Kolkata",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetByIdAsync_ExistingOrg_ReturnsOrgDto()
    {
        var org = MakeOrg();
        _orgRepo.Setup(r => r.GetByIdAsync(org.Id, default)).ReturnsAsync(org);

        var svc = CreateService();
        var result = await svc.GetByIdAsync(org.Id);

        result.Id.Should().Be(org.Id);
        result.Name.Should().Be(org.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingOrg_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        _orgRepo.Setup(r => r.GetByIdAsync(id, default)).ReturnsAsync((Organization?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.GetByIdAsync(id))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateWithAdminAsync_ValidRequest_ReturnsOrgAndAdminUser()
    {
        _passwordService.Setup(p => p.Hash(It.IsAny<string>())).Returns("hash");

        var request = new CreateOrganizationRequest(
            "Acme", "INR", "Asia/Kolkata", "admin@acme.com", "P@ss1!", "Admin", null);

        var svc = CreateService();
        var (org, admin) = await svc.CreateWithAdminAsync(request);

        org.Name.Should().Be("Acme");
        org.DefaultCurrency.Should().Be("INR");
        admin.Email.Should().Be("admin@acme.com");
        admin.Role.Should().Be("admin");
        admin.IsActive.Should().BeTrue();
        admin.OrganizationId.Should().Be(org.Id);
    }

    [Fact]
    public async Task CreateWithAdminAsync_AddsOrganizationAndUserOutboxEvents()
    {
        _passwordService.Setup(p => p.Hash(It.IsAny<string>())).Returns("hash");

        var request = new CreateOrganizationRequest(
            "Acme", "INR", "Asia/Kolkata", "admin@acme.com", "P@ss1!", "Admin", null);

        var svc = CreateService();
        await svc.CreateWithAdminAsync(request);

        _outboxRepo.Verify(r => r.Add(It.Is<IdentityApi.Domain.Entities.OutboxEvent>(e => e.EventType == "organization.created")), Times.Once);
        _outboxRepo.Verify(r => r.Add(It.Is<IdentityApi.Domain.Entities.OutboxEvent>(e => e.EventType == "user.created")), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ExistingOrg_UpdatesAllFields()
    {
        var org = MakeOrg();
        _orgRepo.Setup(r => r.GetByIdAsync(org.Id, default)).ReturnsAsync(org);

        var svc = CreateService();
        var request = new UpdateOrganizationRequest("New Name", "USD", "America/New_York");
        var result = await svc.UpdateAsync(org.Id, request, Guid.NewGuid());

        result.Name.Should().Be("New Name");
        result.DefaultCurrency.Should().Be("USD");
        result.Timezone.Should().Be("America/New_York");
        _orgRepo.Verify(r => r.Update(org), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_PartialRequest_OnlyUpdatesProvidedFields()
    {
        var org = MakeOrg();
        _orgRepo.Setup(r => r.GetByIdAsync(org.Id, default)).ReturnsAsync(org);

        var svc = CreateService();
        var request = new UpdateOrganizationRequest("New Name", null, null);
        var result = await svc.UpdateAsync(org.Id, request, Guid.NewGuid());

        result.Name.Should().Be("New Name");
        result.DefaultCurrency.Should().Be("INR");  // unchanged
        result.Timezone.Should().Be("Asia/Kolkata"); // unchanged
    }

    [Fact]
    public async Task UpdateAsync_NonExistingOrg_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        _orgRepo.Setup(r => r.GetByIdAsync(id, default)).ReturnsAsync((Organization?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.UpdateAsync(id, new UpdateOrganizationRequest(null, null, null), Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }
}
