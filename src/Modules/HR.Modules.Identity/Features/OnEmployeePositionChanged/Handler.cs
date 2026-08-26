using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.OnEmployeePositionChanged;

// IAM-03: when an employee transfers to a different position profile, their inherited roles must
// be recalculated — the previous position assignment stops granting access (expired, not deleted,
// so it remains visible as history) and the new one starts granting access immediately. Direct
// UserRoles and EmployeeRoleOverrides are untouched by this handler, so a valid override always
// survives a transfer (see IdentityAuthorizationService.GetEffectiveRolesAsync, which layers
// overrides on top of the merged position+direct role set every time it is evaluated — there is no
// separate "recalculation" step to run).
internal sealed class Handler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher,
    PositionSync positionSync) : IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>
{
    public async Task HandleAsync(EmployeePositionChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();

        var previousAssignment = await db.UserPositions.FirstOrDefaultAsync(
            up => up.UserId == integrationEvent.EmployeeId &&
                  up.PositionId == integrationEvent.PreviousPositionProfileId,
            cancellationToken);

        // Historical/expired position assignments must cease granting access — expiring here
        // (rather than deleting) keeps the row as an auditable record of when the employee held
        // that position. Idempotent: a row already expired at or before `now` is left untouched.
        if (previousAssignment is not null && previousAssignment.IsActive(now))
            previousAssignment.SetExpiry(now);

        var newPosition = await positionSync.EnsureExistsAsync(
            integrationEvent.CompanyId, integrationEvent.NewPositionProfileId, now, cancellationToken);

        // If the new position profile cannot be resolved for this company right now (e.g. a
        // delivery race against the profile's own lifecycle), there is no Position row to satisfy
        // the UserPosition -> Position foreign key — skip creating/reopening the new assignment
        // rather than let the whole SaveChangesAsync below fail, which would otherwise also roll
        // back the previous assignment's expiry set above. The previous assignment is still
        // expired and persisted; the new assignment will be picked up by the reconciliation pass
        // (IdentityModule.ReconcilePositionRoleAssignmentsAsync) or a later, resolvable event.
        if (newPosition is not null)
        {
            var newAssignment = await db.UserPositions.FirstOrDefaultAsync(
                up => up.UserId == integrationEvent.EmployeeId &&
                      up.PositionId == integrationEvent.NewPositionProfileId,
                cancellationToken);

            if (newAssignment is null)
            {
                db.UserPositions.Add(UserPosition.Create(integrationEvent.EmployeeId, integrationEvent.NewPositionProfileId, now));
            }
            else if (!newAssignment.IsActive(now))
            {
                // Re-assigned back to a position previously held and since expired — reopen it rather
                // than inserting a duplicate composite-key row.
                newAssignment.ClearExpiry();
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var beforeRoleIds = await db.PositionRoles
            .Where(pr => pr.PositionId == integrationEvent.PreviousPositionProfileId)
            .Select(pr => pr.RoleId)
            .ToListAsync(cancellationToken);

        var afterRoleIds = await db.PositionRoles
            .Where(pr => pr.PositionId == integrationEvent.NewPositionProfileId)
            .Select(pr => pr.RoleId)
            .ToListAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new EmployeeInheritedRolesRecalculatedAuditEvent(
                integrationEvent.CompanyId,
                integrationEvent.EmployeeId,
                integrationEvent.PreviousPositionProfileId,
                integrationEvent.NewPositionProfileId,
                beforeRoleIds,
                afterRoleIds,
                now),
            cancellationToken);
    }
}
