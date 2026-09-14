using Hangfire;
using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.OnEmployeeDepartureFinalised;

// P1 fix: this is now the ONLY place that reacts to an employee's departure to actually disable
// the linked ApplicationUser — replacing the former Features/OnOffboardingPlanCompleted trigger,
// which incorrectly tied disablement to offboarding-plan completion instead of the authoritative
// departure decision (see EmployeeDepartureFinalizer, the sole publisher of this event).
//
// AccessDisabled already encodes both "the departure was finalised" AND "the company has
// auto-disable-on-leaving-date enabled" — Identity must not re-derive that policy itself (it has
// no access to Companies settings). When AccessDisabled is false (auto-disable off, or a manual
// DisableUser/EnableUser action already governs the account), this handler is a deliberate no-op.
//
// Durability: rather than disabling ApplicationUser.IsActive directly inline (which would leave no
// trace of a mid-flight failure — this handler's own exceptions are only logged, never retried, by
// IntegrationEventPublisher), this writes a durable AccountDisablement record and enqueues
// AccountDisablementJob to perform and confirm the actual update, with Hangfire's own retry policy.
// Idempotent: a unique index on ApplicationUserId means re-delivery of this event (duplicate
// integration event delivery, or ReconcileFormerEmployeeAccessJob republishing for the same
// employee) never creates a second disablement attempt.
internal sealed class Handler(
    IdentityDbContext db,
    IClock clock,
    IBackgroundJobClient backgroundJobClient) : IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>
{
    public async Task HandleAsync(
        EmployeeDepartureFinalisedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (!integrationEvent.AccessDisabled)
            return;

        // By convention in this system, ApplicationUser.Id == EmployeeId (see
        // Features/OnOffboardingPlanCompleted's former handler and AcceptInvite).
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == integrationEvent.EmployeeId, cancellationToken);

        if (user is null || !user.IsActive)
            return;

        var alreadyRequested = await db.AccountDisablements
            .AnyAsync(d => d.ApplicationUserId == user.Id, cancellationToken);
        if (alreadyRequested)
            return;

        var now = clock.UtcNow;
        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), integrationEvent.CompanyId, user.Id, integrationEvent.EmployeeId, now);

        db.AccountDisablements.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<AccountDisablementJob>(
            job => job.ProcessAsync(request.Id, integrationEvent.CompanyId));
    }
}
