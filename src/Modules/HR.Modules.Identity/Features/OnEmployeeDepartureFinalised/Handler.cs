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

        // By convention in this system, ApplicationUser.Id / UserProfile.Id == EmployeeId (see
        // Features/OnOffboardingPlanCompleted's former handler and AcceptInvite).
        //
        // Ticket 10 (P1): this handler previously only ever looked in db.Users, so a
        // profile-only (real Supabase-backed) account never got a disablement request created for
        // it at all — AccountDisablementJob already knew how to disable a UserProfile, but was
        // never enqueued for one, because this method returned early first. Resolve either backing
        // model and only skip when neither account exists, or the one that does is already inactive.
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == integrationEvent.EmployeeId, cancellationToken);
        var profile = user is null
            ? await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == integrationEvent.EmployeeId, cancellationToken)
            : null;

        if (user is null && profile is null)
            return;

        var isAlreadyInactive = user is { IsActive: false } || profile is { IsActive: false };
        if (isAlreadyInactive)
            return;

        var accountId = user?.Id ?? profile!.Id;

        var alreadyRequested = await db.AccountDisablements
            .AnyAsync(d => d.ApplicationUserId == accountId, cancellationToken);
        if (alreadyRequested)
            return;

        var now = clock.UtcNow;
        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), integrationEvent.CompanyId, accountId, integrationEvent.EmployeeId, now);

        db.AccountDisablements.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        // Ticket 10 (P1): enqueue is best-effort — a process interruption between the SaveChangesAsync
        // above and this call would previously leave the request permanently Pending. Recovery for
        // that case is provided by AccountDisablementReconciliationJob (see IdentityModule's recurring
        // job registration), which sweeps stale Pending/Processing/Failed requests and re-enqueues them.
        backgroundJobClient.Enqueue<AccountDisablementJob>(
            job => job.ProcessAsync(request.Id, integrationEvent.CompanyId));
    }
}
