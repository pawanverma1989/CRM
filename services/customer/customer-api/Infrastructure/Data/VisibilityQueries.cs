namespace CustomerApi.Infrastructure.Data;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;

/// <summary>
/// The single place the visibility rule lives (§2, NFR-6, docs/architecture.md §8):
/// <c>organization_id = claim AND deleted_at IS NULL</c> and, unless the caller is an admin,
/// <c>owner_id IS NULL OR owner_id = ANY(visible_owner_ids)</c>. Records with no owner are
/// visible to everyone. A record the caller cannot see is reported as 404, never 403.
/// </summary>
public static class VisibilityQueries
{
    public static IQueryable<Company> VisibleCompanies(this IQueryable<Company> query, IRequestContext ctx)
    {
        query = query.Where(c => c.OrganizationId == ctx.OrganizationId && c.DeletedAt == null);
        var owners = ctx.VisibleOwnerIds;
        if (owners is not null)
            query = query.Where(c => c.OwnerId == null || owners.Contains(c.OwnerId.Value));
        return query;
    }

    public static IQueryable<Contact> VisibleContacts(this IQueryable<Contact> query, IRequestContext ctx)
    {
        query = query.Where(c => c.OrganizationId == ctx.OrganizationId && c.DeletedAt == null);
        var owners = ctx.VisibleOwnerIds;
        if (owners is not null)
            query = query.Where(c => c.OwnerId == null || owners.Contains(c.OwnerId.Value));
        return query;
    }

    /// <summary>
    /// Deleted companies still inside the recycle-bin window (DEL-2, admin only).
    /// <paramref name="restorableSince"/> is <c>now - Customer:RecycleBinRetentionDays</c>: the
    /// window is enforced here, by the listing and the restore path alike, so a record deleted 31
    /// days ago is never offered as restorable even if the nightly purge has not run yet (AC-15).
    /// Merge losers are excluded — a merge cannot be undone (DUP-6).
    /// </summary>
    public static IQueryable<Company> DeletedCompanies(
        this IQueryable<Company> query, IRequestContext ctx, DateTimeOffset restorableSince)
        => query.Where(c => c.OrganizationId == ctx.OrganizationId
                         && c.DeletedAt != null
                         && c.DeletedAt >= restorableSince
                         && c.MergedIntoId == null);

    /// <summary>Deleted contacts still inside the recycle-bin window (DEL-2, admin only). See <see cref="DeletedCompanies"/>.</summary>
    public static IQueryable<Contact> DeletedContacts(
        this IQueryable<Contact> query, IRequestContext ctx, DateTimeOffset restorableSince)
        => query.Where(c => c.OrganizationId == ctx.OrganizationId
                         && c.DeletedAt != null
                         && c.DeletedAt >= restorableSince
                         && c.MergedIntoId == null);

    /// <summary>True when the caller may own / be assigned this record's owner slot.</summary>
    public static bool CanSeeOwner(this IRequestContext ctx, Guid? ownerId)
        => ownerId is null || ctx.VisibleOwnerIds is null || ctx.VisibleOwnerIds.Contains(ownerId.Value);
}
