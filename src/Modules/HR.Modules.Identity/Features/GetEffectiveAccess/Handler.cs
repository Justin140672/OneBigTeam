using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.GetEffectiveAccess;

internal sealed class GetEffectiveAccessHandler(
    IdentityDbContext db,
    IEmployeeNameReader employeeNameReader,
    ITargetUserCompanyGuard targetUserCompanyGuard,
    IAuthorizationService authorizationService,
    IClock clock)
{
    private sealed record InheritedRoleRow(Guid RoleId, Guid PositionId, string PositionName);

    public async Task<Result<GetEffectiveAccessResponse>> HandleAsync(GetEffectiveAccessRequest request, CancellationToken cancellationToken)
    {
        // IAM-05: prove the target employee actually belongs to the route company before resolving
        // anything about them, same non-disclosing guard every other user-administration handler uses.
        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.EmployeeId, cancellationToken);
        if (!isMember)
            return Result.Failure<GetEffectiveAccessResponse>(Error.NotFound("No user found for this employee."));

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, [request.EmployeeId], cancellationToken);
        var employeeName = names.TryGetValue(request.EmployeeId, out var resolvedName) ? resolvedName : string.Empty;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.EmployeeId, cancellationToken);

        // All active position assignments (company-scoped via the joined Position row).
        var activePositions = await db.UserPositions
            .AsNoTracking()
            .Where(up => up.UserId == request.EmployeeId && (up.ExpiresAt == null || up.ExpiresAt > now))
            .Join(db.Positions.Where(p => p.CompanyId == request.CompanyId),
                up => up.PositionId,
                p => p.Id,
                (up, p) => new { up.AssignedAt, Position = p })
            .ToListAsync(cancellationToken);

        PositionSummaryDto? position = activePositions
            .OrderByDescending(x => x.AssignedAt)
            .Select(x => new PositionSummaryDto(x.Position.Id, x.Position.Name))
            .FirstOrDefault();

        var positionIds = activePositions.Select(x => x.Position.Id).Distinct().ToList();

        var positionRoles = positionIds.Count == 0
            ? []
            : await db.PositionRoles
                .AsNoTracking()
                .Where(pr => positionIds.Contains(pr.PositionId))
                .ToListAsync(cancellationToken);

        var positionNameLookup = activePositions
            .Select(x => x.Position)
            .DistinctBy(p => p.Id)
            .ToDictionary(p => p.Id, p => p.Name);

        var inheritedRows = positionRoles
            .Select(pr => new InheritedRoleRow(pr.RoleId, pr.PositionId, positionNameLookup[pr.PositionId]))
            .ToList();

        var directRoleIds = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == request.EmployeeId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);
        var directRoleIdSet = directRoleIds.ToHashSet();

        var overrides = await db.EmployeeRoleOverrides
            .AsNoTracking()
            .Where(o => o.UserId == request.EmployeeId && o.CompanyId == request.CompanyId)
            .ToListAsync(cancellationToken);

        var effectiveRoleIds = await authorizationService.GetEffectiveRolesAsync(request.EmployeeId, cancellationToken);
        var effectivePermissionIds = await authorizationService.GetEffectivePermissionsAsync(request.EmployeeId, cancellationToken);

        var allRoleIds = new HashSet<Guid>(directRoleIds);
        allRoleIds.UnionWith(inheritedRows.Select(r => r.RoleId));
        allRoleIds.UnionWith(overrides.Select(o => o.RoleId));
        allRoleIds.UnionWith(effectiveRoleIds);

        var roleNameLookup = await db.Roles
            .AsNoTracking()
            .Where(r => allRoleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var directRoles = directRoleIds
            .Select(id => new RoleSummaryDto(id, roleNameLookup.GetValueOrDefault(id, string.Empty)))
            .ToList();

        var inheritedRoles = inheritedRows
            .Select(r => new InheritedRoleDto(r.RoleId, roleNameLookup.GetValueOrDefault(r.RoleId, string.Empty), r.PositionId, r.PositionName))
            .ToList();

        var overrideDtos = overrides
            .Select(o => new RoleOverrideDto(
                o.Id,
                o.RoleId,
                roleNameLookup.GetValueOrDefault(o.RoleId, string.Empty),
                o.OverrideType.ToString(),
                o.Reason,
                o.ExpiresAt,
                o.IsActive(now)))
            .ToList();

        // Origins per role: everything that legitimately contributes to a role being effective.
        var roleOrigins = new Dictionary<Guid, List<string>>();
        foreach (var roleId in effectiveRoleIds)
        {
            var origins = new List<string>();

            if (directRoleIdSet.Contains(roleId))
                origins.Add("Direct");

            foreach (var positionName in inheritedRows.Where(r => r.RoleId == roleId).Select(r => r.PositionName).Distinct())
                origins.Add($"Position:{positionName}");

            var hasActiveGrantOverride = overrides.Any(o =>
                o.RoleId == roleId && o.OverrideType == EmployeeRoleOverrideType.Grant && o.IsActive(now));
            if (hasActiveGrantOverride)
                origins.Add("Override");

            roleOrigins[roleId] = origins;
        }

        var effectiveRoles = effectiveRoleIds
            .Select(id => new EffectiveRoleDto(id, roleNameLookup.GetValueOrDefault(id, string.Empty), roleOrigins.GetValueOrDefault(id, [])))
            .OrderBy(r => r.RoleName, StringComparer.Ordinal)
            .ToList();

        // Effective permissions: everything granted by an effective role, with per-source attribution.
        var effectiveRoleIdList = effectiveRoleIds.ToList();
        var grantingRolePermissions = effectiveRoleIdList.Count == 0
            ? []
            : await db.RolePermissions
                .AsNoTracking()
                .Where(rp => effectiveRoleIdList.Contains(rp.RoleId))
                .ToListAsync(cancellationToken);

        var permissionIdsInvolved = grantingRolePermissions.Select(rp => rp.PermissionId).Distinct().ToList();
        var permissionLookup = permissionIdsInvolved.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Permissions
                .AsNoTracking()
                .Where(p => permissionIdsInvolved.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

        var effectivePermissions = grantingRolePermissions
            .GroupBy(rp => rp.PermissionId)
            .Select(g =>
            {
                var permissionName = permissionLookup.GetValueOrDefault(g.Key, string.Empty);
                var sources = g
                    .SelectMany(rp => roleOrigins.GetValueOrDefault(rp.RoleId, [])
                        .Select(origin => new PermissionSourceDto(rp.RoleId, roleNameLookup.GetValueOrDefault(rp.RoleId, string.Empty), origin)))
                    .ToList();

                return new EffectivePermissionDto(g.Key, permissionName, PermissionScopeResolver.Resolve(permissionName), sources);
            })
            .OrderBy(p => p.PermissionName, StringComparer.Ordinal)
            .ToList();

        // Denied permissions: roles that would apply from direct assignment/position inheritance but
        // were removed from the effective set — under the current model this only happens via an
        // active Deny override (see IdentityAuthorizationService.GetEffectiveRolesAsync).
        var baseRoleIds = new HashSet<Guid>(directRoleIds);
        baseRoleIds.UnionWith(inheritedRows.Select(r => r.RoleId));
        var deniedRoleIds = baseRoleIds.Except(effectiveRoleIds).ToList();

        var deniedPermissions = new List<DeniedPermissionDto>();
        if (deniedRoleIds.Count > 0)
        {
            var deniedRolePermissions = await db.RolePermissions
                .AsNoTracking()
                .Where(rp => deniedRoleIds.Contains(rp.RoleId))
                .ToListAsync(cancellationToken);

            var deniedPermissionIds = deniedRolePermissions.Select(rp => rp.PermissionId).Distinct().ToList();
            var deniedPermissionLookup = deniedPermissionIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await db.Permissions
                    .AsNoTracking()
                    .Where(p => deniedPermissionIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

            foreach (var deniedRoleId in deniedRoleIds)
            {
                var matchingOverride = overrides.FirstOrDefault(o =>
                    o.RoleId == deniedRoleId && o.OverrideType == EmployeeRoleOverrideType.Deny && o.IsActive(now));
                if (matchingOverride is null)
                    continue; // defensive: shouldn't happen under the current model.

                foreach (var rp in deniedRolePermissions.Where(rp => rp.RoleId == deniedRoleId))
                {
                    if (effectivePermissionIds.Contains(rp.PermissionId))
                        continue; // still granted through some other still-effective role.

                    var permissionName = deniedPermissionLookup.GetValueOrDefault(rp.PermissionId, string.Empty);
                    deniedPermissions.Add(new DeniedPermissionDto(
                        rp.PermissionId,
                        permissionName,
                        PermissionScopeResolver.Resolve(permissionName),
                        deniedRoleId,
                        roleNameLookup.GetValueOrDefault(deniedRoleId, string.Empty),
                        matchingOverride.Id,
                        matchingOverride.Reason));
                }
            }
        }

        return Result.Success(new GetEffectiveAccessResponse(
            request.EmployeeId,
            user?.Id,
            employeeName,
            position,
            directRoles,
            inheritedRoles,
            overrideDtos,
            effectiveRoles,
            effectivePermissions,
            deniedPermissions));
    }
}
