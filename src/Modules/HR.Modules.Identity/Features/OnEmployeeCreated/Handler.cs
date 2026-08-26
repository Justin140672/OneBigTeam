using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.OnEmployeeCreated;

// IAM-03: a newly created employee with a PositionProfileId immediately inherits that position's
// default roles. By convention in this system, ApplicationUser.Id == EmployeeId (see
// UserInvite.EmployeeId doc comment and AcceptInvite) — the same convention Features/OnOffboardingPlanCompleted
// relies on — so EmployeeId is used directly as the identity.user_positions.user_id.
//
// This handler only maintains the UserPosition row; it must not create an ApplicationUser (a new
// employee has no login/user account yet — that only happens via InviteEmployeeUser/AcceptInvite).
// The UserPosition row is written regardless, so that once/if the employee later gets a user
// account, GetEffectiveRolesAsync already finds the correct inherited position immediately.
internal sealed class Handler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    PositionSync positionSync) : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.PositionProfileId is null)
            return;

        var positionId = integrationEvent.PositionProfileId.Value;
        var now = clock.UtcNowOffset();

        var alreadyAssigned = await db.UserPositions
            .AnyAsync(up => up.UserId == integrationEvent.EmployeeId && up.PositionId == positionId, cancellationToken);
        if (alreadyAssigned)
            return; // Idempotent — repeat delivery of the same event must not duplicate/reset the assignment.

        await positionSync.EnsureExistsAsync(integrationEvent.CompanyId, positionId, now, cancellationToken);

        db.UserPositions.Add(UserPosition.Create(integrationEvent.EmployeeId, positionId, now));
        await db.SaveChangesAsync(cancellationToken);

        var grantedRoleIds = await db.PositionRoles
            .Where(pr => pr.PositionId == positionId)
            .Select(pr => pr.RoleId)
            .ToListAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeInheritedRolesRecalculatedAuditEvent(
                integrationEvent.CompanyId,
                integrationEvent.EmployeeId,
                PreviousPositionId: null,
                NewPositionId: positionId,
                BeforeRoleIds: [],
                AfterRoleIds: grantedRoleIds,
                now),
            cancellationToken);
    }
}
