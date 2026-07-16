using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity;

internal sealed class IdentityAuthorizationService(
    IdentityDbContext db,
    IClock clock) : IAuthorizationService
{
    public async Task<bool> HasPermissionAsync(
        Guid userId,
        Guid permissionId,
        CancellationToken ct = default)
    {
        var permissions = await GetEffectivePermissionsAsync(userId, ct);
        return permissions.Contains(permissionId);
    }

    public async Task<IReadOnlySet<Guid>> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var effectiveRoles = await GetEffectiveRolesAsync(userId, ct);

        if (effectiveRoles.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var permissionIds = await db.RolePermissions
            .Where(rp => effectiveRoles.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync(ct);

        return permissionIds.ToHashSet();
    }

    public async Task<IReadOnlySet<Guid>> GetEffectiveRolesAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        // 1. Roles inherited from active position assignments.
         var positionRoleIds = await db.UserPositions
            .Where(up => up.UserId == userId &&
                         (up.ExpiresAt == null || up.ExpiresAt > now))
            .Join(db.PositionRoles,
                up => up.PositionId,
                pr => pr.PositionId,
                (up, pr) => pr.RoleId)
            .Distinct()
            .ToListAsync(ct);

        // 2. Direct user-role assignments.
        var directRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        // 3. Merge base set.
        var effectiveRoles = new HashSet<Guid>(positionRoleIds);
        effectiveRoles.UnionWith(directRoleIds);

        // 4. Apply employee-level overrides (Deny removes, Grant adds).
        var overrides = await db.EmployeeRoleOverrides
            .Where(o => o.UserId == userId)
            .ToListAsync(ct);

        foreach (var @override in overrides)
        {
            if (@override.OverrideType == EmployeeRoleOverrideType.Deny)
            {
                effectiveRoles.Remove(@override.RoleId);
            }
            else if (@override.OverrideType == EmployeeRoleOverrideType.Grant)
            {
                effectiveRoles.Add(@override.RoleId);
            }
        }

        return effectiveRoles;
    }
}
