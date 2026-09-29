using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ListEmployeeRoleOverrides;

internal sealed class ListEmployeeRoleOverridesHandler(
    IdentityDbContext db,
    ITargetUserCompanyGuard targetUserCompanyGuard)
{
    public async Task<Result<ListEmployeeRoleOverridesResponse>> HandleAsync(
        ListEmployeeRoleOverridesRequest request, CancellationToken cancellationToken)
    {
        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.UserId, cancellationToken);
        if (!isMember)
            return Result.Failure<ListEmployeeRoleOverridesResponse>(Error.NotFound("User was not found."));

        var overrides = await db.EmployeeRoleOverrides
            .Where(o => o.UserId == request.UserId && o.CompanyId == request.CompanyId)
            .OrderByDescending(o => o.AssignedAt)
            .Select(o => new EmployeeRoleOverrideItem(
                o.RoleId, o.OverrideType, o.Reason, o.ExpiresAt, o.AssignedAt, o.AssignedBy))
            .ToListAsync(cancellationToken);

        return Result.Success(new ListEmployeeRoleOverridesResponse(overrides));
    }
}
