using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ListPositionRoleDefaults;

// IAM-03: lists every active position profile in the company (via IPositionProfileReader — the
// authoritative source, owned by HR.Modules.Employees) alongside whatever default RoleIds have
// been configured for it in identity.position_roles (empty list if none configured yet). This is
// the read side administrators use before calling Features/SetPositionRoleDefaults.
internal sealed class ListPositionRoleDefaultsHandler(
    IdentityDbContext db,
    IPositionProfileReader positionProfileReader)
{
    public async Task<Result<ListPositionRoleDefaultsResponse>> HandleAsync(
        ListPositionRoleDefaultsRequest request, CancellationToken cancellationToken)
    {
        var activeIds = await positionProfileReader.GetAllActiveIdsAsync(request.CompanyId, cancellationToken);

        if (activeIds.Count == 0)
            return Result.Success(new ListPositionRoleDefaultsResponse([]));

        var summaries = await positionProfileReader.GetSummariesAsync(request.CompanyId, activeIds, cancellationToken);

        var roleIdsByPosition = await db.PositionRoles
            .Where(pr => activeIds.Contains(pr.PositionId))
            .GroupBy(pr => pr.PositionId)
            .Select(g => new { PositionId = g.Key, RoleIds = g.Select(pr => pr.RoleId).ToList() })
            .ToDictionaryAsync(x => x.PositionId, x => x.RoleIds, cancellationToken);

        var items = summaries
            .Select(s => new PositionRoleDefaultItem(
                s.Id,
                s.Title,
                s.IsActive,
                roleIdsByPosition.TryGetValue(s.Id, out var roleIds) ? roleIds : []))
            .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result.Success(new ListPositionRoleDefaultsResponse(items));
    }
}
