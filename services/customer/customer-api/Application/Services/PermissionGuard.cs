namespace CustomerApi.Application.Services;
using CustomerApi.Application.Exceptions;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;

/// <summary>
/// §2 permission table. Visibility itself is enforced by <see cref="VisibilityQueries"/> — a
/// record the caller cannot view is never loaded, so it surfaces as 404 and its existence stays
/// hidden. These guards cover the actions a role may never perform, which are genuine 403s.
/// </summary>
public static class PermissionGuard
{
    /// <summary>
    /// A manager may edit anything they can view; a sales rep may edit their own records and
    /// unowned ones (records with no owner are visible to everyone — §10 assumption).
    /// </summary>
    public static void EnsureCanEdit(IRequestContext ctx, Guid? ownerId)
    {
        if (ctx.IsManagerOrAbove) return;
        if (ownerId is null || ownerId == ctx.ActorUserId) return;

        throw new ForbiddenException("A sales rep may only edit records they own.");
    }

    /// <summary>OWN-2: only an admin or a manager may set or change a record's owner.</summary>
    public static void EnsureCanAssignOwner(IRequestContext ctx, Guid? newOwnerId)
    {
        if (!ctx.IsManagerOrAbove)
            throw new ForbiddenException("Only an admin or a manager may change a record's owner.");

        if (!ctx.CanSeeOwner(newOwnerId))
            throw new CustomerValidationException("ownerId", "You may not assign this owner.");
    }

    /// <summary>
    /// §2: a sales rep becomes the owner of what they create. An explicit, different owner is an
    /// action they may not perform, not a validation problem.
    /// </summary>
    public static Guid? ResolveOwnerOnCreate(IRequestContext ctx, Guid? requested)
    {
        if (ctx.IsManagerOrAbove)
        {
            if (requested is not null && !ctx.CanSeeOwner(requested))
                throw new CustomerValidationException("ownerId", "You may not assign this owner.");
            return requested;
        }

        if (requested is not null && requested != ctx.ActorUserId)
            throw new ForbiddenException("A sales rep becomes the owner of the records they create.");

        return ctx.ActorUserId;
    }
}
