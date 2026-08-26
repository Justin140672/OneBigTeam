using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.SetPositionRoleDefaults;

// IAM-03: configures the set of roles automatically inherited by every employee holding a given
// position (HR.Modules.Employees PositionProfile). Reuses IAM-02's RoleAdministrationPolicy so an
// administrator can never configure a position default that grants a role they are not themselves
// authorised to administer (e.g. an HR Administrator cannot make Company Administrator a position
// default) — same privilege-escalation guard as Features/UpdateUserRoles, applied here because a
// position default is just as capable of granting a role as a direct per-user assignment.
internal sealed class SetPositionRoleDefaultsHandler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    IAuthorizationService authorizationService,
    IPositionProfileReader positionProfileReader,
    PositionSync positionSync)
{
    public async Task<Result<SetPositionRoleDefaultsResponse>> HandleAsync(
        SetPositionRoleDefaultsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var profileExists = await positionProfileReader.ExistsAsync(
            request.CompanyId, request.PositionProfileId, cancellationToken);
        if (!profileExists)
            return Result.Failure<SetPositionRoleDefaultsResponse>(
                Error.NotFound($"Position profile '{request.PositionProfileId}' was not found."));

        var requestedRoleIds = request.RoleIds.Distinct().ToList();

        var validRoleIds = await db.Roles
            .Where(r => requestedRoleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (validRoleIds.Count != requestedRoleIds.Count)
            return Result.Failure<SetPositionRoleDefaultsResponse>(Error.Validation("One or more roles do not exist."));

        var actorEffectiveRoles = actorUserId.HasValue
            ? await authorizationService.GetEffectiveRolesAsync(actorUserId.Value, cancellationToken)
            : new HashSet<Guid>();
        var administrableRoles = RoleAdministrationPolicy.GetAdministrableRoles(actorEffectiveRoles);

        var unauthorizedRoleIds = requestedRoleIds.Where(id => !administrableRoles.Contains(id)).ToList();
        if (unauthorizedRoleIds.Count > 0)
            return Result.Failure<SetPositionRoleDefaultsResponse>(
                Error.Forbidden("You are not authorised to set one or more of the requested roles as a position default."));

        var now = clock.UtcNowOffset();

        // Ensures identity.positions has a row for this PositionProfile before the FK-constrained
        // position_roles insert below — see PositionSync's remarks.
        await positionSync.EnsureExistsAsync(request.CompanyId, request.PositionProfileId, now, cancellationToken);

        var currentRoles = await db.PositionRoles
            .Where(pr => pr.PositionId == request.PositionProfileId)
            .ToListAsync(cancellationToken);
        var beforeRoleIds = currentRoles.Select(pr => pr.RoleId).ToList();

        var toRemove = currentRoles.Where(pr => !requestedRoleIds.Contains(pr.RoleId)).ToList();
        var toAdd = requestedRoleIds.Where(id => !beforeRoleIds.Contains(id)).ToList();

        db.PositionRoles.RemoveRange(toRemove);
        foreach (var roleId in toAdd)
            db.PositionRoles.Add(PositionRole.Create(request.PositionProfileId, roleId, now));

        await db.SaveChangesAsync(cancellationToken);

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            await auditEventPublisher.PublishAsync(
                new PositionRoleDefaultsChangedAuditEvent(
                    request.CompanyId, request.PositionProfileId, beforeRoleIds, requestedRoleIds, actorUserId, now),
                cancellationToken);
        }

        return Result.Success(new SetPositionRoleDefaultsResponse(request.PositionProfileId, requestedRoleIds));
    }
}
