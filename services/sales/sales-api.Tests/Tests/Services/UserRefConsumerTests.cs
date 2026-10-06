namespace SalesApi.Tests.Services;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalesApi.Application.Events;
using SalesApi.Application.Services;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using SalesApi.Tests.TestSupport;
using Xunit;

/// <summary>
/// user.* consumption into user_refs, driven through the real LookupRepository on EF InMemory.
/// The processed_events claim is simulated with a set keyed by event_id, which is what the
/// INSERT ... ON CONFLICT DO NOTHING does in PostgreSQL.
/// </summary>
public class UserRefConsumerTests
{
    private static readonly Guid OrgId = Guid.Parse("8f1d8a52-6a0e-4c43-9a51-0b9f6f1a2c01");
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static (InboundEventProcessor processor, SalesDbContext context) Build()
    {
        var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var claimed = new HashSet<Guid>();
        var tx = new Mock<ITransactionScope>();
        tx.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tx.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tx.Object);
        uow.Setup(u => u.ExecuteSqlAsync(It.IsAny<FormattableString>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((FormattableString sql, CancellationToken _) =>
               claimed.Add((Guid)sql.GetArgument(0)!) ? 1 : 0);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
           .Returns((CancellationToken ct) => context.SaveChangesAsync(ct));

        var processor = new InboundEventProcessor(
            context,
            new LookupRepository(context),
            Mock.Of<IDsrService>(),
            uow.Object,
            new FixedTimeProvider(Now),
            NullLogger<InboundEventProcessor>.Instance);

        return (processor, context);
    }

    /// <summary>The envelope identity publishes, including the resync flag and empty changes.</summary>
    private static string IdentityEnvelope(
        Guid eventId, string eventType, Guid userId, int version,
        string? firstName, string? lastName, string email = "person@example.com",
        string status = "active", bool resync = false)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["event_id"] = eventId,
            ["event_type"] = eventType,
            ["organization_id"] = OrgId,
            ["aggregate_type"] = "user",
            ["aggregate_id"] = userId,
            ["version"] = version,
            ["occurred_at"] = Now,
            ["actor_id"] = Guid.NewGuid(),
            ["payload"] = new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["email"] = email,
                ["first_name"] = firstName,
                ["last_name"] = lastName,
                ["role"] = "sales_rep",
                ["team_id"] = null,
                ["status"] = status,
                ["version"] = version,
                ["actor_id"] = Guid.NewGuid(),
                ["resync"] = resync,
                ["changes"] = Array.Empty<object>()
            }
        });

    [Fact]
    public async Task UserCreated_WithEmptyNames_FallsBackToUserIdNeverEmail()
    {
        var (processor, context) = Build();
        var userId = Guid.NewGuid();

        var result = await processor.ProcessAsync(
            IdentityEnvelope(Guid.NewGuid(), EventTypes.UserCreated, userId, 1, "", "  ", email: "hidden@example.com"),
            EventTypes.UserCreated, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);
        var row = await context.UserRefs.SingleAsync(u => u.UserId == userId);
        row.DisplayName.Should().Be(userId.ToString());
        row.DisplayName.Should().NotContain("@");
    }

    [Fact]
    public async Task UserCreated_WithNames_UsesFirstAndLastName()
    {
        var (processor, context) = Build();
        var userId = Guid.NewGuid();

        await processor.ProcessAsync(
            IdentityEnvelope(Guid.NewGuid(), EventTypes.UserCreated, userId, 1, "Asha", "Rao"),
            EventTypes.UserCreated, CancellationToken.None);

        (await context.UserRefs.SingleAsync(u => u.UserId == userId)).DisplayName.Should().Be("Asha Rao");
    }

    [Fact]
    public async Task ResyncUserUpdated_WhenUserIsMissing_InsertsUserRef()
    {
        var (processor, context) = Build();
        var userId = Guid.NewGuid();

        var result = await processor.ProcessAsync(
            IdentityEnvelope(Guid.NewGuid(), EventTypes.UserUpdated, userId, 4, "Asha", "Rao", resync: true),
            EventTypes.UserUpdated, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);
        var row = await context.UserRefs.SingleAsync(u => u.UserId == userId);
        row.DisplayName.Should().Be("Asha Rao");
        row.OrganizationId.Should().Be(OrgId);
        row.IsActive.Should().BeTrue();
        row.SourceVersion.Should().Be(4);
    }

    [Fact]
    public async Task ResyncUserUpdated_WithVersionEqualToSourceVersion_ChangesNothing()
    {
        var (processor, context) = Build();
        var userId = Guid.NewGuid();
        var earlier = Now.AddDays(-1);
        context.UserRefs.Add(new UserRef
        {
            UserId = userId, OrganizationId = OrgId, DisplayName = "Original Name",
            IsActive = true, SourceVersion = 4, UpdatedAt = earlier
        });
        await context.SaveChangesAsync();

        var result = await processor.ProcessAsync(
            IdentityEnvelope(Guid.NewGuid(), EventTypes.UserUpdated, userId, 4, "Renamed", "Person",
                status: "deactivated", resync: true),
            EventTypes.UserUpdated, CancellationToken.None);

        result.Should().Be(InboundResult.Handled);
        var row = await context.UserRefs.AsNoTracking().SingleAsync(u => u.UserId == userId);
        row.DisplayName.Should().Be("Original Name");
        row.IsActive.Should().BeTrue();
        row.SourceVersion.Should().Be(4);
        row.UpdatedAt.Should().Be(earlier);
    }

    [Fact]
    public async Task ResyncUserUpdated_WithNewerVersion_AppliesUpdate()
    {
        var (processor, context) = Build();
        var userId = Guid.NewGuid();
        context.UserRefs.Add(new UserRef
        {
            UserId = userId, OrganizationId = OrgId, DisplayName = "Original Name",
            IsActive = true, SourceVersion = 4, UpdatedAt = Now.AddDays(-1)
        });
        await context.SaveChangesAsync();

        await processor.ProcessAsync(
            IdentityEnvelope(Guid.NewGuid(), EventTypes.UserUpdated, userId, 5, "Renamed", "Person", resync: true),
            EventTypes.UserUpdated, CancellationToken.None);

        var row = await context.UserRefs.AsNoTracking().SingleAsync(u => u.UserId == userId);
        row.DisplayName.Should().Be("Renamed Person");
        row.SourceVersion.Should().Be(5);
    }

    [Fact]
    public async Task ResyncUserUpdated_DeliveredTwice_SecondDeliveryIsDuplicate()
    {
        var (processor, context) = Build();
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var body = IdentityEnvelope(eventId, EventTypes.UserUpdated, userId, 2, "Asha", "Rao", resync: true);

        var first = await processor.ProcessAsync(body, EventTypes.UserUpdated, CancellationToken.None);
        var second = await processor.ProcessAsync(body, EventTypes.UserUpdated, CancellationToken.None);

        first.Should().Be(InboundResult.Handled);
        second.Should().Be(InboundResult.Duplicate);
        (await context.UserRefs.CountAsync(u => u.UserId == userId)).Should().Be(1);
    }

    [Fact]
    public async Task UserUpdated_WithVersionZero_IsStillApplied()
    {
        var (processor, context) = Build();
        var userId = Guid.NewGuid();
        context.UserRefs.Add(new UserRef
        {
            UserId = userId, OrganizationId = OrgId, DisplayName = "Original Name",
            IsActive = true, SourceVersion = 3, UpdatedAt = Now.AddDays(-1)
        });
        await context.SaveChangesAsync();

        await processor.ProcessAsync(
            IdentityEnvelope(Guid.NewGuid(), EventTypes.UserUpdated, userId, 0, "Renamed", "Person"),
            EventTypes.UserUpdated, CancellationToken.None);

        (await context.UserRefs.AsNoTracking().SingleAsync(u => u.UserId == userId))
            .DisplayName.Should().Be("Renamed Person");
    }
}
