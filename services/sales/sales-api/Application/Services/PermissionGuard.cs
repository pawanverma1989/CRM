namespace SalesApi.Application.Services;
using SalesApi.Infrastructure.Repositories;

/// <summary>
/// Visibility and permission guards (CLAUDE.md §6).
/// A record the caller cannot see is reported as 404, never 403 (AC-12).
/// </summary>
public static class PermissionGuard
{
    /// <summary>
    /// A manager may edit anything they can view; a sales rep may edit their own deals and unowned ones.
    /// </summary>
    public static void EnsureCanEdit(IRequestContext ctx, Guid? ownerId)
    {
        if (ctx.IsManagerOrAbove) return;
        if (ownerId is null || ownerId == ctx.ActorUserId) return;

        throw new ForbiddenException("A sales rep may only edit deals they own.");
    }

    /// <summary>Only an admin or manager may change a deal's owner.</summary>
    public static void EnsureCanAssignOwner(IRequestContext ctx, Guid? newOwnerId)
    {
        if (!ctx.IsManagerOrAbove)
            throw new ForbiddenException("Only an admin or a manager may change a deal's owner.");

        if (!ctx.CanSeeOwner(newOwnerId))
            throw new SalesValidationException("ownerId", "You may not assign this owner.");
    }

    /// <summary>A sales rep becomes the owner of what they create.</summary>
    public static Guid? ResolveOwnerOnCreate(IRequestContext ctx, Guid? requested)
    {
        if (ctx.IsManagerOrAbove)
        {
            if (requested is not null && !ctx.CanSeeOwner(requested))
                throw new SalesValidationException("ownerId", "You may not assign this owner.");
            return requested;
        }

        if (requested is not null && requested != ctx.ActorUserId)
            throw new ForbiddenException("A sales rep becomes the owner of the deals they create.");

        return ctx.ActorUserId;
    }
}
