using Hangfire;
using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Jobs;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.DeactivateLeavePolicyAssignmentOnEmployeeDeparture;

// Closes the gap left after the LEAVE-03 leave-year rollover job was introduced: without this,
// LeaveYearRolloverService would keep generating a brand-new policy-year balance (with
// carry-over) for terminated employees, because it worked off "who had a balance last year"
// rather than "who is currently active". Deactivating the EmployeeLeavePolicyAssignment here makes
// that distinction available to LeaveYearRolloverService, which now requires an active assignment
// before rolling an employee's balance forward.
//
// Follow-up reliability fix: this handler used to deactivate the assignment inline. But
// HR.SharedKernel.IntegrationEventPublisher deliberately catches and only logs a handler's
// exception (so one failing consumer never blocks another or the publishing caller) — meaning a
// transient failure here was silently dropped with no retry, while Employees'
// EmployeeLeavingProcess.FinalisationCompletedAt was still correctly marked (it only guarantees
// the publish call returned to every handler, not that each handler's own work completed — see
// that property's remarks). Instead, this now records a durable, retryable
// LeavePolicyDeactivationOnDeparture request (mirrors HR.Modules.Identity's AccountDisablement /
// AccountDisablementJob pattern) and enqueues LeavePolicyDeactivationJob to perform and confirm the
// actual deactivation, with Hangfire's own retry policy plus a daily reconciliation sweep
// (ReconcileLeavePolicyDeactivationsJob) for anything still stuck.
//
// Idempotent: a unique index on (company_id, employee_id) means re-delivery of this event never
// creates a second deactivation request.
internal sealed class EmployeeDepartureFinalisedHandler(
    LeaveDbContext dbContext,
    IClock clock,
    IBackgroundJobClient backgroundJobClient) : IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeDepartureFinalisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.EmployeeLeavePolicyAssignments
            .FirstOrDefaultAsync(
                a => a.CompanyId == integrationEvent.CompanyId && a.EmployeeId == integrationEvent.EmployeeId,
                cancellationToken);

        if (assignment is null || !assignment.IsActive)
            return;

        var alreadyRequested = await dbContext.LeavePolicyDeactivationsOnDeparture
            .AnyAsync(
                d => d.CompanyId == integrationEvent.CompanyId && d.EmployeeId == integrationEvent.EmployeeId,
                cancellationToken);
        if (alreadyRequested)
            return;

        var now = clock.UtcNowOffset();
        var request = LeavePolicyDeactivationOnDeparture.CreatePending(
            Guid.NewGuid(), integrationEvent.CompanyId, integrationEvent.EmployeeId, integrationEvent.OccurredAt, now);

        dbContext.LeavePolicyDeactivationsOnDeparture.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<LeavePolicyDeactivationJob>(
            job => job.ProcessAsync(request.Id, integrationEvent.CompanyId));
    }
}
