namespace IdentityApi.Tests.Integration.Database;
using System.Text.Json;
using FluentAssertions;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Interfaces;
using IdentityApi.Application.Services;
using IdentityApi.Domain.Entities;
using IdentityApi.Infrastructure.Data;
using IdentityApi.Infrastructure.HealthChecks;
using IdentityApi.Infrastructure.Repositories;
using IdentityApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

/// <summary>
/// V4 (users.version + bump trigger) against real PostgreSQL, applied as identity_svc, and the
/// guarantee that every user.* event carries the row's version as committed.
/// </summary>
public class UserVersionIntegrationTests(IdentityDbFixture db) : IClassFixture<IdentityDbFixture>
{
    private const string StrongPassword = "Corr3ct-Horse-Battery";

    private static UserService Service(IdentityDbContext context)
    {
        var passwords = new Mock<IPasswordService>();
        passwords.Setup(p => p.Hash(It.IsAny<string>())).Returns("bcrypt_hash");
        return new UserService(new UserRepository(context), new TeamRepository(context), new OutboxRepository(context),
            passwords.Object, new Mock<IEmailService>().Object, context, NullLogger<UserService>.Instance);
    }

    private async Task<(Guid OrgId, Guid AdminId)> SeedOrgAsync()
    {
        await using var context = db.CreateContext();
        var org = new Organization
        {
            Id = Guid.NewGuid(), Name = "Org " + Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var admin = new User
        {
            Id = Guid.NewGuid(), OrganizationId = org.Id, Email = $"admin-{Guid.NewGuid():N}@example.com",
            PasswordHash = "hash", FirstName = "Ada", Role = "admin", Status = "active",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        context.Organizations.Add(org);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        return (org.Id, admin.Id);
    }

    private static int PayloadVersion(OutboxEvent evt)
    {
        using var doc = JsonDocument.Parse(evt.Payload);
        return doc.RootElement.GetProperty("version").GetInt32();
    }

    [Fact]
    public async Task Migrations_AppliedAsIdentitySvc_WhichIsNotSuperuser()
    {
        var super = await db.ScalarAsync<bool>("SELECT rolsuper FROM pg_roles WHERE rolname = current_user");
        var user = await db.ScalarAsync<string>("SELECT current_user::text");

        user.Should().Be("identity_svc");
        super.Should().BeFalse();
    }

    [Fact]
    public async Task UsersVersion_DefaultsToOne_AndTriggerBumpsOnEveryUpdate()
    {
        var (_, adminId) = await SeedOrgAsync();

        (await db.ScalarAsync<int>("SELECT version FROM users WHERE id = @id", ("id", adminId))).Should().Be(1);

        await db.ScalarAsync<object>("UPDATE users SET first_name = 'Ada2' WHERE id = @id", ("id", adminId));
        await db.ScalarAsync<object>("UPDATE users SET version = 99, last_name = 'L' WHERE id = @id", ("id", adminId));

        // A client cannot set the version: the trigger always uses OLD.version + 1.
        (await db.ScalarAsync<int>("SELECT version FROM users WHERE id = @id", ("id", adminId))).Should().Be(3);
    }

    [Fact]
    public async Task CreateAsync_UserCreatedEventVersion_EqualsRowVersion()
    {
        var (orgId, adminId) = await SeedOrgAsync();
        await using var context = db.CreateContext();

        var dto = await Service(context).CreateAsync(
            new CreateUserRequest($"new-{Guid.NewGuid():N}@example.com", StrongPassword, "Bob", null, null, "sales_rep", null),
            orgId, adminId);

        var evt = await context.OutboxEvents.AsNoTracking().SingleAsync(e => e.AggregateId == dto.Id && e.EventType == "user.created");
        var rowVersion = await db.ScalarAsync<int>("SELECT version FROM users WHERE id = @id", ("id", dto.Id));
        rowVersion.Should().Be(1);
        PayloadVersion(evt).Should().Be(rowVersion);
    }

    [Fact]
    public async Task UpdateAsync_UserUpdatedEventVersion_EqualsBumpedRowVersion()
    {
        var (orgId, adminId) = await SeedOrgAsync();
        Guid userId;
        await using (var context = db.CreateContext())
        {
            userId = (await Service(context).CreateAsync(
                new CreateUserRequest($"rep-{Guid.NewGuid():N}@example.com", StrongPassword, "Rep", null, null, "sales_rep", null),
                orgId, adminId)).Id;
        }

        // An update that emits no event (e.g. a login stamp) still bumps the version.
        await db.ScalarAsync<object>("UPDATE users SET last_login_at = now() WHERE id = @id", ("id", userId));

        await using (var context = db.CreateContext())
            await Service(context).UpdateAsync(userId, new UpdateUserRequest(null, null, null, "manager", null, null), orgId, adminId);
        await using (var context = db.CreateContext())
            await Service(context).DeactivateAsync(userId, orgId, adminId);

        await using var read = db.CreateContext();
        var rowVersion = await db.ScalarAsync<int>("SELECT version FROM users WHERE id = @id", ("id", userId));
        var updated = await read.OutboxEvents.AsNoTracking().SingleAsync(e => e.AggregateId == userId && e.EventType == "user.updated");
        var deactivated = await read.OutboxEvents.AsNoTracking().SingleAsync(e => e.AggregateId == userId && e.EventType == "user.deactivated");

        PayloadVersion(updated).Should().Be(3);
        PayloadVersion(deactivated).Should().Be(4);
        rowVersion.Should().Be(4);
    }

    [Fact]
    public async Task ResyncUserEventsAsync_OnPostgres_QueuesCurrentVersionsForOrgOnly()
    {
        var (orgId, adminId) = await SeedOrgAsync();
        var (otherOrgId, _) = await SeedOrgAsync();
        await db.ScalarAsync<object>("UPDATE users SET first_name = 'Ada' WHERE id = @id", ("id", adminId)); // version 2

        int queued;
        await using (var context = db.CreateContext())
            queued = await Service(context).ResyncUserEventsAsync(orgId, adminId);

        await using var read = db.CreateContext();
        var rows = await read.OutboxEvents.AsNoTracking()
            .Where(e => e.EventType == "user.updated" && (e.OrganizationId == orgId || e.OrganizationId == otherOrgId))
            .ToListAsync();

        queued.Should().Be(1);
        rows.Should().ContainSingle().Which.AggregateId.Should().Be(adminId);
        PayloadVersion(rows[0]).Should().Be(2);
    }

    [Fact]
    public async Task OutboxLagHealthCheck_OnPostgres_ReportsDegradedForOldUnpublishedRow()
    {
        await using var context = db.CreateContext();
        context.OutboxEvents.Add(new OutboxEvent
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), AggregateType = "user", AggregateId = Guid.NewGuid(),
            EventType = "user.updated", Payload = "{}", OccurredAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await context.SaveChangesAsync();

        var result = await new OutboxLagHealthCheck(
                new OutboxRepository(context),
                Options.Create(new RabbitMqSettings { OutboxLagWarningSeconds = 60 }),
                TimeProvider.System)
            .CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        ((long)result.Data["oldest_age_seconds"]).Should().BeGreaterThanOrEqualTo(3600);
    }
}
