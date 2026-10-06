namespace SalesApi.Tests.Services;

using System;
using FluentAssertions;
using Moq;
using SalesApi.Application.Exceptions;
using SalesApi.Application.Services;
using SalesApi.Infrastructure.Context;
using Xunit;

public class PermissionGuardTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();
    private static readonly Guid SalesRepId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private static Mock<IRequestContext> BuildCtx(
        string role,
        Guid actorId,
        Guid[]? visibleOwnerIds = null)
    {
        var mock = new Mock<IRequestContext>();
        mock.Setup(c => c.Role).Returns(role);
        mock.Setup(c => c.ActorUserId).Returns(actorId);
        mock.Setup(c => c.IsAdmin).Returns(role == "admin");
        mock.Setup(c => c.IsManagerOrAbove).Returns(role is "admin" or "manager");
        mock.Setup(c => c.IsService).Returns(false);
        mock.Setup(c => c.VisibleOwnerIds).Returns(visibleOwnerIds);
        return mock;
    }

    // ── EnsureCanEdit ──────────────────────────────────────────────────────────

    [Fact]
    public void EnsureCanEdit_Passes_WhenAdmin()
    {
        var ctx = BuildCtx("admin", AdminId);
        var act = () => PermissionGuard.EnsureCanEdit(ctx.Object, OtherId);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanEdit_Passes_WhenSalesRepOwnsTheDeal()
    {
        var ctx = BuildCtx("sales_rep", SalesRepId);
        var act = () => PermissionGuard.EnsureCanEdit(ctx.Object, ownerId: SalesRepId);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanEdit_Passes_WhenDealIsUnowned()
    {
        var ctx = BuildCtx("sales_rep", SalesRepId);
        var act = () => PermissionGuard.EnsureCanEdit(ctx.Object, ownerId: null);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanEdit_Throws_WhenSalesRepDoesNotOwnDeal()
    {
        var ctx = BuildCtx("sales_rep", SalesRepId);
        var act = () => PermissionGuard.EnsureCanEdit(ctx.Object, ownerId: OtherId);
        act.Should().Throw<ForbiddenException>()
            .WithMessage("*A sales rep may only edit deals they own*");
    }

    // ── EnsureCanAssignOwner ───────────────────────────────────────────────────

    [Fact]
    public void EnsureCanAssignOwner_Throws_WhenSalesRep()
    {
        var ctx = BuildCtx("sales_rep", SalesRepId);
        var act = () => PermissionGuard.EnsureCanAssignOwner(ctx.Object, OtherId);
        act.Should().Throw<ForbiddenException>()
            .WithMessage("*Only an admin or a manager*");
    }

    [Fact]
    public void EnsureCanAssignOwner_Throws_WhenManagerCannotSeeNewOwner()
    {
        // Manager has visible owner IDs set, and the requested owner is NOT in the list
        var visibleIds = new[] { ManagerId };
        var ctx = BuildCtx("manager", ManagerId, visibleOwnerIds: visibleIds);

        var act = () => PermissionGuard.EnsureCanAssignOwner(ctx.Object, OtherId); // OtherId not in visible list

        act.Should().Throw<SalesValidationException>()
            .WithMessage("*You may not assign this owner*");
    }

    [Fact]
    public void EnsureCanAssignOwner_Passes_WhenManagerCanSeeOwner()
    {
        // Manager can see ManagerId and OtherId
        var visibleIds = new[] { ManagerId, OtherId };
        var ctx = BuildCtx("manager", ManagerId, visibleOwnerIds: visibleIds);

        var act = () => PermissionGuard.EnsureCanAssignOwner(ctx.Object, OtherId);

        act.Should().NotThrow();
    }

    // ── ResolveOwnerOnCreate ───────────────────────────────────────────────────

    [Fact]
    public void ResolveOwnerOnCreate_ReturnsSalesRepAsSelf_WhenNoOwnerSupplied()
    {
        var ctx = BuildCtx("sales_rep", SalesRepId);

        var result = PermissionGuard.ResolveOwnerOnCreate(ctx.Object, requested: null);

        result.Should().Be(SalesRepId);
    }

    [Fact]
    public void ResolveOwnerOnCreate_Throws_WhenSalesRepTriesToAssignOtherOwner()
    {
        var ctx = BuildCtx("sales_rep", SalesRepId);

        var act = () => PermissionGuard.ResolveOwnerOnCreate(ctx.Object, requested: OtherId);

        act.Should().Throw<ForbiddenException>()
            .WithMessage("*sales rep becomes the owner*");
    }

    [Fact]
    public void ResolveOwnerOnCreate_ReturnsRequestedOwner_WhenManagerSuppliesValidOwner()
    {
        // Manager can see OtherId
        var visibleIds = new[] { ManagerId, OtherId };
        var ctx = BuildCtx("manager", ManagerId, visibleOwnerIds: visibleIds);

        var result = PermissionGuard.ResolveOwnerOnCreate(ctx.Object, requested: OtherId);

        result.Should().Be(OtherId);
    }

    [Fact]
    public void ResolveOwnerOnCreate_Throws_WhenManagerCannotSeeRequestedOwner()
    {
        // Manager can only see themselves
        var visibleIds = new[] { ManagerId };
        var ctx = BuildCtx("manager", ManagerId, visibleOwnerIds: visibleIds);

        var act = () => PermissionGuard.ResolveOwnerOnCreate(ctx.Object, requested: OtherId); // OtherId not visible

        act.Should().Throw<SalesValidationException>()
            .WithMessage("*You may not assign this owner*");
    }
}
