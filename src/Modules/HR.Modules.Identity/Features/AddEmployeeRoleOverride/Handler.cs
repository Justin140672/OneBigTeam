using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.AddEmployeeRoleOverride;

// IAM-04: administers a single employee-level Grant/Deny role override. A user may hold at most
// one override per role (enforced by the unique (user_id, role_id) index) — creating a new
// override for a role that already has one replaces it in place rather than failing, which is how
// "conflicting grant and deny records" are resolved deterministically: the most recent
// administrator action always wins, there is no separate merge/precedence rule to reason about.
internal sealed class AddEmployeeRoleOverrideHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ITargetUserCompanyGuard targetUserCompanyGuard,
    IAuthorizationService authorizationService)
{
    public async Task<Result<AddEmployeeRoleOverrideResponse>> HandleAsync(
        AddEmployeeRoleOverrideRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.UserId, cancellationToken);
        if (!isMember)
            return Result.Failure<AddEmployeeRoleOverrideResponse>(Error.NotFound("User was not found."));

        var roleExists = await db.Roles.AnyAsync(r => r.Id == request.RoleId, cancellationToken);
        if (!roleExists)
            return Result.Failure<AddEmployeeRoleOverrideResponse>(Error.Validation("Role was not found."));

        var now = clock.UtcNowOffset();

        if (request.ExpiresAt is not null && request.ExpiresAt <= now)
            return Result.Failure<AddEmployeeRoleOverrideResponse>(
                Error.Validation("ExpiresAt must be in the future for a temporary override."));

        // IAM-04: self-created elevation overrides are prohibited outright — an administrator must
        // never be able to grant themselves an extra role via an override, regardless of whether
        // that role would otherwise be within their administrable set (the same self-elevation
        // concern IAM-02 already closes for direct role edits, applied to this second grant path).
        // A self-created Deny (voluntarily restricting one's own access) is not an elevation risk
        // and remains allowed.
        if (actorUserId == request.UserId && request.OverrideType == EmployeeRoleOverrideType.Grant)
        {
            await PublishRejectionAsync(request, actorUserId, now, "self_elevation_denied");
            return Result.Failure<AddEmployeeRoleOverrideResponse>(
                Error.Forbidden("You cannot grant yourself a permission override."));
        }

        // Same role-administration boundary as UpdateUserRoles (IAM-02) and SetPositionRoleDefaults
        // (IAM-03) — an HR Administrator cannot grant/deny Company Administrator via an override
        // (or vice versa), closing a third path to the same privilege-escalation problem.
        var actorEffectiveRoles = actorUserId.HasValue
            ? await authorizationService.GetEffectiveRolesAsync(actorUserId.Value, cancellationToken)
            : new HashSet<Guid>();
        var administrableRoles = RoleAdministrationPolicy.GetAdministrableRoles(actorEffectiveRoles);

        if (!administrableRoles.Contains(request.RoleId))
        {
            await PublishRejectionAsync(request, actorUserId, now, "role_not_authorised_to_administer");
            return Result.Failure<AddEmployeeRoleOverrideResponse>(
                Error.Forbidden("You are not authorised to grant or deny this role."));
        }

        var existing = await db.EmployeeRoleOverrides
            .FirstOrDefaultAsync(o => o.UserId == request.UserId && o.RoleId == request.RoleId, cancellationToken);

        if (existing is not null)
            db.EmployeeRoleOverrides.Remove(existing);

        var created = EmployeeRoleOverride.Create(
            request.CompanyId, request.UserId, request.RoleId, request.OverrideType,
            request.Reason.Trim(), request.ExpiresAt, now, actorUserId);

        db.EmployeeRoleOverrides.Add(created);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeRoleOverrideCreatedAuditEvent(
                request.CompanyId,
                request.UserId,
                created.Id,
                request.RoleId,
                request.OverrideType,
                request.Reason.Trim(),
                request.ExpiresAt,
                actorUserId,
                now),
            cancellationToken);

        return Result.Success(new AddEmployeeRoleOverrideResponse(
            request.UserId, request.RoleId, request.OverrideType, created.Reason, created.ExpiresAt));
    }

    private Task PublishRejectionAsync(
        AddEmployeeRoleOverrideRequest request, Guid? actorUserId, DateTimeOffset now, string reason) =>
        auditEventPublisher.PublishAsync(
            new RoleChangeRejectedAuditEvent(
                request.CompanyId,
                request.UserId,
                request.UserId,
                reason,
                [request.RoleId],
                actorUserId,
                now),
            CancellationToken.None);
}
