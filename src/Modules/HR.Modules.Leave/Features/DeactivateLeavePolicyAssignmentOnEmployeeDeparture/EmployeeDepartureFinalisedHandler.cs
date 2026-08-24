using HR.Modules.Employees.Contracts;
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
// Historical LeaveBalance/LeaveBalanceAdjustment rows are intentionally left untouched — leavers
// keep their balance history, they simply stop receiving new ones.
//
// Idempotent: EmployeeLeavePolicyAssignment.Deactivate() is a no-op if already inactive, so
// redelivery of this event (or an amended/re-finalised departure) causes no further changes.
internal sealed class EmployeeDepartureFinalisedHandler(LeaveDbContext dbContext)
    : IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>
{
    public async Task HandleAsync(EmployeeDepartureFinalisedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var assignment = await dbContext.EmployeeLeavePolicyAssignments
            .FirstOrDefaultAsync(
                a => a.CompanyId == integrationEvent.CompanyId && a.EmployeeId == integrationEvent.EmployeeId,
                cancellationToken);

        if (assignment is null || !assignment.IsActive)
            return;

        assignment.Deactivate(integrationEvent.OccurredAt);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
