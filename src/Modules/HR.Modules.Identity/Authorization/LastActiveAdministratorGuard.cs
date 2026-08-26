using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Authorization;

/// <summary>
/// IAM-02: shared "would this leave the company with zero active holders of a lockout-protected
/// role?" check, used both when removing a role directly (Features/UpdateUserRoles) and when
/// disabling a user's account outright (Features/DisableUser) — either action can accidentally
/// strand a company with no active Company Administrator or HR Administrator.
///
/// "Active" mirrors ListUsersHandler's existing convention: an ApplicationUser (local-auth account)
/// is active when IsActive is true; a Supabase-backed UserProfile has no local disable concept, so
/// its mere existence counts as active.
/// </summary>
internal sealed class LastActiveAdministratorGuard(IdentityDbContext db, IEmployeeAudienceReader employeeAudienceReader)
{
    public async Task<bool> HasOtherActiveHolderAsync(
        Guid companyId, Guid roleId, Guid excludeUserId, CancellationToken cancellationToken)
    {
        var companyEmployeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(companyId, cancellationToken);
        return await HasOtherActiveHolderAsync(roleId, excludeUserId, companyEmployeeIds, cancellationToken);
    }

    public async Task<bool> HasOtherActiveHolderAsync(
        Guid roleId, Guid excludeUserId, IReadOnlyList<Guid> companyEmployeeIds, CancellationToken cancellationToken)
    {
        var otherHolderIds = await db.UserRoles
            .Where(ur => ur.RoleId == roleId && ur.UserId != excludeUserId && companyEmployeeIds.Contains(ur.UserId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (otherHolderIds.Count == 0)
            return false;

        var hasActiveApplicationUser = await db.Users
            .AnyAsync(u => otherHolderIds.Contains(u.Id) && u.IsActive, cancellationToken);

        if (hasActiveApplicationUser)
            return true;

        var hasProfile = await db.UserProfiles
            .AnyAsync(p => otherHolderIds.Contains(p.Id), cancellationToken);

        return hasProfile;
    }
}
