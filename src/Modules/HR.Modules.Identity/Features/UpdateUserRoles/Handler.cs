using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.UpdateUserRoles;

internal sealed class UpdateUserRolesHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    ITargetUserCompanyGuard targetUserCompanyGuard,
    IAuthorizationService authorizationService,
    IEmployeeAudienceReader employeeAudienceReader,
    LastActiveAdministratorGuard lastActiveAdministratorGuard)
{
    public async Task<Result<UpdateUserRolesResponse>> HandleAsync(
        UpdateUserRolesRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        // IAM-01: the target user id must belong to the route company — otherwise a valid user id
        // from another company could have its roles read/changed cross-tenant.
        var isMember = await targetUserCompanyGuard.IsMemberAsync(request.CompanyId, request.UserId, cancellationToken);
        if (!isMember)
            return Result.Failure<UpdateUserRolesResponse>(Error.NotFound("User was not found."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            return Result.Failure<UpdateUserRolesResponse>(Error.NotFound("User was not found."));

        var requestedRoleIds = request.RoleIds.Distinct().ToList();
        if (requestedRoleIds.Count == 0)
            return Result.Failure<UpdateUserRolesResponse>(
                Error.Validation("A user must always retain at least one role."));

        var validRoleIds = await db.Roles
            .Where(r => requestedRoleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (validRoleIds.Count != requestedRoleIds.Count)
            return Result.Failure<UpdateUserRolesResponse>(Error.Validation("One or more roles do not exist."));

        var currentRoles = await db.UserRoles
            .Where(ur => ur.UserId == request.UserId)
            .ToListAsync(cancellationToken);
        var currentRoleIds = currentRoles.Select(ur => ur.RoleId).ToList();

        var now = clock.UtcNow;

        // IAM-02: the mandatory Employee role can never be removed through this API — it is the
        // floor role core session endpoints (GetMe, GetCompany, etc.) depend on.
        if (!requestedRoleIds.Contains(SystemRoles.Employee))
        {
            await PublishRejectionAsync(
                request, requestedRoleIds, actorUserId, now, "employee_role_is_mandatory");
            return Result.Failure<UpdateUserRolesResponse>(
                Error.Validation("The Employee role is mandatory and cannot be removed."));
        }

        var toRemove = currentRoles.Where(ur => !requestedRoleIds.Contains(ur.RoleId)).ToList();
        var toAdd = requestedRoleIds.Where(id => !currentRoleIds.Contains(id)).ToList();
        var changedRoleIds = toAdd.Concat(toRemove.Select(ur => ur.RoleId)).Distinct().ToList();

        if (changedRoleIds.Count > 0)
        {
            // IAM-02: privilege-escalation / role-administration-boundary guard. This applies
            // whether the actor is editing another user or themselves — self-elevation is simply
            // the case where request.UserId == actorUserId, and it's covered by the same check
            // rather than a special case, so it can never be bypassed by a future policy change
            // that lets more roles call this endpoint.
            var actorEffectiveRoles = actorUserId.HasValue
                ? await authorizationService.GetEffectiveRolesAsync(actorUserId.Value, cancellationToken)
                : new HashSet<Guid>();
            var administrableRoles = RoleAdministrationPolicy.GetAdministrableRoles(actorEffectiveRoles);

            var unauthorizedRoleIds = changedRoleIds.Where(id => !administrableRoles.Contains(id)).ToList();
            if (unauthorizedRoleIds.Count > 0)
            {
                var reason = actorUserId == request.UserId
                    ? "self_elevation_denied"
                    : "role_not_authorised_to_administer";
                await PublishRejectionAsync(request, requestedRoleIds, actorUserId, now, reason);
                return Result.Failure<UpdateUserRolesResponse>(
                    Error.Forbidden("You are not authorised to grant or remove one or more of the requested roles."));
            }
        }

        // IAM-02: last-active-administrator lockout. Removing a lockout-protected role (Company
        // Administrator, HR Administrator) is blocked if the target is the last active holder of
        // that role in the company — evaluated per role, independently, so a multi-role initial
        // company creator (Employee + Company Administrator + HR Administrator, see SignUp) can
        // still have one of those roles removed as long as they are not the sole remaining holder
        // of that specific role. There is no implicit coupling between the two admin roles.
        var removedProtectedRoleIds = toRemove
            .Select(ur => ur.RoleId)
            .Where(RoleAdministrationPolicy.IsLockoutProtected)
            .Distinct()
            .ToList();

        if (removedProtectedRoleIds.Count > 0)
        {
            var companyEmployeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(request.CompanyId, cancellationToken);

            foreach (var protectedRoleId in removedProtectedRoleIds)
            {
                var hasOtherActiveHolder = await lastActiveAdministratorGuard.HasOtherActiveHolderAsync(
                    protectedRoleId, request.UserId, companyEmployeeIds, cancellationToken);

                if (!hasOtherActiveHolder)
                {
                    await PublishRejectionAsync(request, requestedRoleIds, actorUserId, now, "last_active_administrator");
                    return Result.Failure<UpdateUserRolesResponse>(
                        Error.Conflict("This user is the last active holder of a protected administrator role and cannot have it removed."));
                }
            }
        }

        db.UserRoles.RemoveRange(toRemove);
        foreach (var roleId in toAdd)
            db.UserRoles.Add(UserRole.Create(request.UserId, roleId, now));

        await db.SaveChangesAsync(cancellationToken);

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            await auditEventPublisher.PublishAsync(
                new UserRolesChangedAuditEvent(
                    request.CompanyId,
                    request.UserId,
                    request.UserId, // ApplicationUser.Id == EmployeeId by convention
                    currentRoleIds,
                    requestedRoleIds,
                    actorUserId,
                    now),
                cancellationToken);
        }

        return Result.Success(new UpdateUserRolesResponse(request.UserId, requestedRoleIds));
    }

    private Task PublishRejectionAsync(
        UpdateUserRolesRequest request,
        IReadOnlyList<Guid> requestedRoleIds,
        Guid? actorUserId,
        DateTimeOffset now,
        string reason) =>
        auditEventPublisher.PublishAsync(
            new RoleChangeRejectedAuditEvent(
                request.CompanyId,
                request.UserId,
                request.UserId,
                reason,
                requestedRoleIds,
                actorUserId,
                now),
            CancellationToken.None); // audited even if the caller's cancellation token is later triggered
}
