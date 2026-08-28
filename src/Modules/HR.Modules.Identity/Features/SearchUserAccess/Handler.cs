using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.SearchUserAccess;

/// <summary>
/// IAM-08: batch (not per-user) equivalent of GetEffectiveAccess's direct/inherited/override
/// resolution, built to answer "which users match this role/position/override criteria" across the
/// whole company in one pass rather than one authorization-service call per employee.
/// </summary>
internal sealed class SearchUserAccessHandler(
    IdentityDbContext db,
    IEmployeeNameReader employeeNameReader,
    IEmployeeAudienceReader employeeAudienceReader,
    IClock clock)
{
    private static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(14);

    public async Task<SearchUserAccessResponse> HandleAsync(SearchUserAccessRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var expiringSoonCutoff = now + ExpiringSoonWindow;

        var employeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(request.CompanyId, cancellationToken);
        if (employeeIds.Count == 0)
            return new SearchUserAccessResponse([], 0, request.Page, request.PageSize);

        var users = await db.Users.AsNoTracking().Where(u => employeeIds.Contains(u.Id)).ToListAsync(cancellationToken);
        var usersById = users.ToDictionary(u => u.Id);

        var profiles = await db.UserProfiles.AsNoTracking().Where(p => employeeIds.Contains(p.Id)).ToListAsync(cancellationToken);
        var profilesById = profiles.ToDictionary(p => p.Id);

        var directRoles = await db.UserRoles.AsNoTracking().Where(ur => employeeIds.Contains(ur.UserId)).ToListAsync(cancellationToken);

        var activePositions = await db.UserPositions
            .AsNoTracking()
            .Where(up => employeeIds.Contains(up.UserId) && (up.ExpiresAt == null || up.ExpiresAt > now))
            .Join(db.Positions.Where(p => p.CompanyId == request.CompanyId),
                up => up.PositionId, p => p.Id, (up, p) => new { up.UserId, Position = p })
            .ToListAsync(cancellationToken);

        var positionIds = activePositions.Select(x => x.Position.Id).Distinct().ToList();
        var positionRoles = positionIds.Count == 0
            ? []
            : await db.PositionRoles.AsNoTracking().Where(pr => positionIds.Contains(pr.PositionId)).ToListAsync(cancellationToken);
        var positionNameLookup = activePositions.Select(x => x.Position).DistinctBy(p => p.Id).ToDictionary(p => p.Id, p => p.Name);

        var overrides = await db.EmployeeRoleOverrides
            .AsNoTracking()
            .Where(o => o.CompanyId == request.CompanyId && employeeIds.Contains(o.UserId))
            .ToListAsync(cancellationToken);

        var allRoleIds = new HashSet<Guid>(directRoles.Select(r => r.RoleId));
        allRoleIds.UnionWith(positionRoles.Select(r => r.RoleId));
        allRoleIds.UnionWith(overrides.Select(o => o.RoleId));
        var roleNameLookup = await db.Roles.AsNoTracking()
            .Where(r => allRoleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, employeeIds, cancellationToken);

        var items = new List<UserAccessSearchItem>();

        foreach (var employeeId in employeeIds)
        {
            usersById.TryGetValue(employeeId, out var user);
            profilesById.TryGetValue(employeeId, out var profile);
            if (user is null && profile is null)
                continue; // No account of any kind — nothing to report on for access search.

            var directForUser = directRoles.Where(r => r.UserId == employeeId)
                .Select(r => new RoleRef(r.RoleId, roleNameLookup.GetValueOrDefault(r.RoleId, string.Empty)))
                .ToList();

            var positionsForUser = activePositions.Where(p => p.UserId == employeeId).Select(p => p.Position.Id).ToHashSet();
            var inheritedForUser = positionRoles.Where(pr => positionsForUser.Contains(pr.PositionId))
                .Select(pr => new InheritedRoleRef(
                    pr.RoleId, roleNameLookup.GetValueOrDefault(pr.RoleId, string.Empty),
                    pr.PositionId, positionNameLookup.GetValueOrDefault(pr.PositionId, string.Empty)))
                .ToList();

            var overridesForUser = overrides.Where(o => o.UserId == employeeId)
                .Select(o => new OverrideRef(
                    o.Id, o.RoleId, roleNameLookup.GetValueOrDefault(o.RoleId, string.Empty),
                    o.OverrideType.ToString(), o.ExpiresAt,
                    o.ExpiresAt is not null && o.ExpiresAt > now && o.ExpiresAt <= expiringSoonCutoff))
                .ToList();

            if (request.RoleId is { } roleFilter &&
                !directForUser.Any(r => r.RoleId == roleFilter) &&
                !inheritedForUser.Any(r => r.RoleId == roleFilter) &&
                !overridesForUser.Any(o => o.RoleId == roleFilter))
                continue;

            if (request.PositionId is { } positionFilter && !positionsForUser.Contains(positionFilter))
                continue;

            var matchesOverrideState = request.OverrideState switch
            {
                OverrideStateFilter.HasGrantOverride => overridesForUser.Any(o => o.OverrideType == nameof(EmployeeRoleOverrideType.Grant)),
                OverrideStateFilter.HasDenyOverride => overridesForUser.Any(o => o.OverrideType == nameof(EmployeeRoleOverrideType.Deny)),
                OverrideStateFilter.HasAnyOverride => overridesForUser.Count > 0,
                OverrideStateFilter.HasExpiringOverride => overridesForUser.Any(o => o.IsExpiringSoon),
                _ => true,
            };
            if (!matchesOverrideState)
                continue;

            var name = names.TryGetValue(employeeId, out var employeeName)
                ? employeeName
                : user is not null ? $"{user.FirstName} {user.LastName}".Trim()
                : $"{profile!.FirstName} {profile.LastName}".Trim();
            var email = user?.Email ?? profile?.Email ?? string.Empty;

            items.Add(new UserAccessSearchItem(
                employeeId, user?.Id ?? profile?.Id,
                string.IsNullOrWhiteSpace(name) ? email : name, email,
                directForUser, inheritedForUser, overridesForUser));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            items = items.Where(i =>
                    i.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    i.Email.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        items = items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var total = items.Count;
        var page = items.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();

        return new SearchUserAccessResponse(page, total, request.Page, request.PageSize);
    }
}
