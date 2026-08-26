using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.ListEmployeeRoleOverrides;

// IAM-04: lists every currently-stored override (active and expired-but-not-yet-swept) for a
// target user, only returned when the caller has role-administration permission (users:manage,
// declared at the endpoint) — access to another user's overrides is never available without it.
internal sealed class ListEmployeeRoleOverridesHandler(
    IdentityDbContext db,
    ITargetUserCompanyGuard targetUserCompanyGuard)
{
    public async Task<Result<ListEmployeeRoleOverridesResponse>> HandleAsync(
        ListEmployeeRoleOverridesRequest request, CancellationToken cancellationToken)
    {
        // IAM-01-style guard: the target user id must belong to the route company.
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
