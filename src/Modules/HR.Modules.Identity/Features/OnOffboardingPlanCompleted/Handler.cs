using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.OnOffboardingPlanCompleted;

// Automatically disables an employee's user account once their offboarding plan completes.
// There is no prior automatic-disable-on-offboarding wiring in this codebase — ApplicationUser.Deactivate()
// previously existed but was never called from anywhere. This is the first caller.
//
// By convention in this system, ApplicationUser.Id == EmployeeId (see UserInvite.EmployeeId doc
// comment and AcceptInvite), so the integration event's EmployeeId is looked up directly as the
// user id. If no linked user exists (employee never accepted an invite), this is a no-op.
internal sealed class Handler(
    IdentityDbContext db,
    IClock clock,
    IAuditEventPublisher auditEventPublisher) : IIntegrationEventHandler<OffboardingPlanCompletedIntegrationEvent>
{
    public async Task HandleAsync(OffboardingPlanCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == integrationEvent.EmployeeId, cancellationToken);

        if (user is null || !user.IsActive)
            return;

        var now = clock.UtcNow;
        user.Deactivate(now);
        await db.SaveChangesAsync(cancellationToken);

        await auditEventPublisher.PublishAsync(
            new UserAutoDisabledOnOffboardingAuditEvent(
                integrationEvent.CompanyId,
                user.Id,
                integrationEvent.EmployeeId,
                now),
            cancellationToken);
    }
}
