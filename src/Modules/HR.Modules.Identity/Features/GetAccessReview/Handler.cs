using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.GetAccessReview;

/// <summary>
/// IAM-08: builds the access-review report — every user who holds a privileged role (any role
/// other than the baseline Employee role) via direct assignment, position inheritance, or an
/// active Grant override, together with the source of each. Reuses the same
/// direct/inherited/override resolution GetEffectiveAccessHandler performs for a single user
/// (IAM-05), batched for the whole company instead of one authorization-service call per employee.
/// </summary>
internal sealed class GetAccessReviewHandler(
    IdentityDbContext db,
    IEmployeeNameReader employeeNameReader,
    IEmployeeAudienceReader employeeAudienceReader,
    IClock clock)
{
    private static readonly TimeSpan ExpiringSoonWindow = TimeSpan.FromDays(14);

    public async Task<GetAccessReviewResponse> HandleAsync(GetAccessReviewRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var expiringSoonCutoff = now + ExpiringSoonWindow;

        var employeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(request.CompanyId, cancellationToken);
        if (employeeIds.Count == 0)
            return new GetAccessReviewResponse([], 0);

        var users = await db.Users.AsNoTracking().Where(u => employeeIds.Contains(u.Id)).ToListAsync(cancellationToken);
        var usersById = users.ToDictionary(u => u.Id);
        var profiles = await db.UserProfiles.AsNoTracking().Where(p => employeeIds.Contains(p.Id)).ToListAsync(cancellationToken);
        var profilesById = profiles.ToDictionary(p => p.Id);

        var directRoles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => employeeIds.Contains(ur.UserId) && ur.RoleId != SystemRoles.Employee)
            .ToListAsync(cancellationToken);

        var activePositions = await db.UserPositions
            .AsNoTracking()
            .Where(up => employeeIds.Contains(up.UserId) && (up.ExpiresAt == null || up.ExpiresAt > now))
            .Join(db.Positions.Where(p => p.CompanyId == request.CompanyId),
                up => up.PositionId, p => p.Id, (up, p) => new { up.UserId, Position = p })
            .ToListAsync(cancellationToken);

        var positionIds = activePositions.Select(x => x.Position.Id).Distinct().ToList();
        var positionRoles = positionIds.Count == 0
            ? []
            : await db.PositionRoles
                .AsNoTracking()
                .Where(pr => positionIds.Contains(pr.PositionId) && pr.RoleId != SystemRoles.Employee)
                .ToListAsync(cancellationToken);
        var positionNameLookup = activePositions.Select(x => x.Position).DistinctBy(p => p.Id).ToDictionary(p => p.Id, p => p.Name);

        var grantOverrides = await db.EmployeeRoleOverrides
            .AsNoTracking()
            .Where(o => o.CompanyId == request.CompanyId && employeeIds.Contains(o.UserId)
                && o.OverrideType == EmployeeRoleOverrideType.Grant && o.RoleId != SystemRoles.Employee)
            .ToListAsync(cancellationToken);
        // Active-only: an expired grant override no longer confers any privilege.
        grantOverrides = grantOverrides.Where(o => o.IsActive(now)).ToList();

        var allRoleIds = new HashSet<Guid>(directRoles.Select(r => r.RoleId));
        allRoleIds.UnionWith(positionRoles.Select(r => r.RoleId));
        allRoleIds.UnionWith(grantOverrides.Select(o => o.RoleId));
        var roleNameLookup = await db.Roles.AsNoTracking()
            .Where(r => allRoleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, employeeIds, cancellationToken);

        var items = new List<AccessReviewItem>();

        foreach (var employeeId in employeeIds)
        {
            usersById.TryGetValue(employeeId, out var user);
            profilesById.TryGetValue(employeeId, out var profile);
            if (user is null && profile is null)
                continue;

            var privileges = new List<PrivilegeSourceItem>();

            privileges.AddRange(directRoles.Where(r => r.UserId == employeeId)
                .Select(r => new PrivilegeSourceItem(
                    r.RoleId, roleNameLookup.GetValueOrDefault(r.RoleId, string.Empty), "Direct", null, false)));

            var positionsForUser = activePositions.Where(p => p.UserId == employeeId).Select(p => p.Position.Id).ToHashSet();
            privileges.AddRange(positionRoles.Where(pr => positionsForUser.Contains(pr.PositionId))
                .Select(pr => new PrivilegeSourceItem(
                    pr.RoleId, roleNameLookup.GetValueOrDefault(pr.RoleId, string.Empty),
                    $"Position:{positionNameLookup.GetValueOrDefault(pr.PositionId, string.Empty)}", null, false)));

            privileges.AddRange(grantOverrides.Where(o => o.UserId == employeeId)
                .Select(o => new PrivilegeSourceItem(
                    o.RoleId, roleNameLookup.GetValueOrDefault(o.RoleId, string.Empty), "Override",
                    o.ExpiresAt, o.ExpiresAt is not null && o.ExpiresAt <= expiringSoonCutoff)));

            if (privileges.Count == 0)
                continue; // Not a privileged user — baseline Employee access only.

            var name = names.TryGetValue(employeeId, out var employeeName)
                ? employeeName
                : user is not null ? $"{user.FirstName} {user.LastName}".Trim()
                : $"{profile!.FirstName} {profile.LastName}".Trim();
            var email = user?.Email ?? profile?.Email ?? string.Empty;

            items.Add(new AccessReviewItem(
                employeeId, user?.Id ?? profile?.Id,
                string.IsNullOrWhiteSpace(name) ? email : name, email,
                privileges.OrderBy(p => p.RoleName, StringComparer.Ordinal).ToList()));
        }

        items = items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return new GetAccessReviewResponse(items, items.Count);
    }
}
