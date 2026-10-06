namespace CustomerApi.Tests.Integration.Api;
using CustomerApi.Application.Services;
using CustomerApi.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// user.* consumption into user_refs on the real customer_db: the envelope identity publishes
/// (with the resync flag and empty changes), version handling, idempotency, and that user_refs
/// never ends up holding an email.
/// </summary>
public class UserRefConsumerTests(CustomerApiFixture fixture) : ApiTestBase(fixture)
{
    private static object IdentityPayload(
        Guid userId, int version, string? firstName, string? lastName,
        string email = "person@example.com", string status = "active", bool resync = false)
        => new
        {
            user_id = userId,
            email,
            first_name = firstName,
            last_name = lastName,
            role = "sales_rep",
            team_id = (Guid?)null,
            status,
            version,
            actor_id = Guid.NewGuid(),
            resync,
            changes = Array.Empty<object>()
        };

    private Task<CustomerApi.Domain.Entities.UserRef?> UserRefAsync(Guid userId)
        => Fixture.WithDbAsync(db => db.UserRefs.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId));

    [Fact]
    public async Task UserCreated_WithEmptyNames_FallsBackToUserIdNeverEmail()
    {
        var userId = Guid.NewGuid();

        var result = await DeliverAsync("user.created",
            IdentityPayload(userId, 1, "", " ", email: "hidden@example.com"), version: 1, aggregateId: userId);

        result.Should().Be(InboundResult.Handled);
        var row = await UserRefAsync(userId);
        row!.DisplayName.Should().Be(userId.ToString());
        row.DisplayName.Should().NotContain("@");
    }

    [Fact]
    public async Task UserCreated_WithEmailShapedDisplayNameOnly_StillFallsBackToUserId()
    {
        var userId = Guid.NewGuid();

        await DeliverAsync("user.created",
            new { user_id = userId, display_name = "hidden@example.com", email = "hidden@example.com" },
            version: 1, aggregateId: userId);

        (await UserRefAsync(userId))!.DisplayName.Should().Be(userId.ToString());
    }

    [Fact]
    public async Task ResyncUserUpdated_WhenUserIsMissing_InsertsUserRef()
    {
        var userId = Guid.NewGuid();

        var result = await DeliverAsync("user.updated",
            IdentityPayload(userId, 4, "Asha", "Rao", resync: true), version: 4, aggregateId: userId);

        result.Should().Be(InboundResult.Handled);
        var row = await UserRefAsync(userId);
        row.Should().NotBeNull();
        row!.DisplayName.Should().Be("Asha Rao");
        row.OrganizationId.Should().Be(OrganizationId);
        row.IsActive.Should().BeTrue();
        row.SourceVersion.Should().Be(4);
    }

    [Fact]
    public async Task ResyncUserUpdated_WithVersionEqualToSourceVersion_ChangesNothing()
    {
        // RepAId is seeded by ApiTestBase as "Rep A" with source_version 1.
        var before = await UserRefAsync(RepAId);

        var result = await DeliverAsync("user.updated",
            IdentityPayload(RepAId, 1, "Renamed", "Person", status: "deactivated", resync: true),
            version: 1, aggregateId: RepAId);

        result.Should().Be(InboundResult.Handled);
        var after = await UserRefAsync(RepAId);
        after!.DisplayName.Should().Be("Rep A");
        after.IsActive.Should().BeTrue();
        after.SourceVersion.Should().Be(1);
        after.UpdatedAt.Should().Be(before!.UpdatedAt);
    }

    [Fact]
    public async Task ResyncUserUpdated_WithNewerVersion_AppliesUpdate()
    {
        await DeliverAsync("user.updated",
            IdentityPayload(RepAId, 2, "Renamed", "Person", resync: true), version: 2, aggregateId: RepAId);

        var row = await UserRefAsync(RepAId);
        row!.DisplayName.Should().Be("Renamed Person");
        row.SourceVersion.Should().Be(2);
    }

    [Fact]
    public async Task ResyncUserUpdated_DeliveredTwice_SecondDeliveryIsDuplicate()
    {
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = IdentityPayload(userId, 3, "Asha", "Rao", resync: true);

        var first = await DeliverAsync("user.updated", payload, eventId: eventId, version: 3, aggregateId: userId);
        var second = await DeliverAsync("user.updated", payload, eventId: eventId, version: 3, aggregateId: userId);

        first.Should().Be(InboundResult.Handled);
        second.Should().Be(InboundResult.Duplicate);
        (await Fixture.WithDbAsync(db => db.UserRefs.CountAsync(u => u.UserId == userId))).Should().Be(1);
    }

    [Fact]
    public async Task UserUpdated_WithVersionZero_IsStillApplied()
    {
        await DeliverAsync("user.updated",
            IdentityPayload(RepAId, 0, "Renamed", "Person"), version: 0, aggregateId: RepAId);

        (await UserRefAsync(RepAId))!.DisplayName.Should().Be("Renamed Person");
    }
}
