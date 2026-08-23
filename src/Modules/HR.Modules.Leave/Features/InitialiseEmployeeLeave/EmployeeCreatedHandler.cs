using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.InitialiseEmployeeLeave;

internal sealed class EmployeeCreatedHandler : IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICompanyLeaveSettingsReader _leaveSettingsReader;

    public EmployeeCreatedHandler(LeaveDbContext dbContext, IClock clock, ICompanyLeaveSettingsReader leaveSettingsReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _leaveSettingsReader = leaveSettingsReader;
    }

    public async Task HandleAsync(EmployeeCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        // Only balance-tracked leave types get a LeaveBalance row (see LeaveType.HasBalance).
        var activeLeaveTypes = await _dbContext.LeaveTypes
            .Where(lt => lt.CompanyId == integrationEvent.CompanyId && lt.IsActive && lt.HasBalance)
            .ToListAsync(cancellationToken);

        if (activeLeaveTypes.Count == 0)
            return;

        var assignment = await _dbContext.EmployeeLeavePolicyAssignments
            .FirstOrDefaultAsync(
                a => a.CompanyId == integrationEvent.CompanyId && a.EmployeeId == integrationEvent.EmployeeId,
                cancellationToken);

        if (assignment is null)
        {
            if (integrationEvent.DefaultLeavePolicyId is null)
                return;

            assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(),
                integrationEvent.CompanyId,
                integrationEvent.EmployeeId,
                integrationEvent.DefaultLeavePolicyId.Value,
                integrationEvent.StartDate,
                _clock.UtcNowOffset());

            _dbContext.EmployeeLeavePolicyAssignments.Add(assignment);
        }

        var leaveSettings = await _leaveSettingsReader.GetLeaveSettingsAsync(integrationEvent.CompanyId, cancellationToken);
        var now = _clock.UtcNowOffset();
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, leaveSettings.LeaveYearStartMonth);
        var (policyYearStart, policyYearEnd) = LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);

        // Idempotency guard (per 04-event-architecture.md — integration event consumers must
        // tolerate repeated/duplicate delivery). Without this check, a redelivered
        // EmployeeCreatedIntegrationEvent would add a second full set of LeaveBalance rows for the
        // same employee/policy year, which the Leave Summary Report then silently sums together —
        // inflating the displayed entitlement (e.g. 25 real days appearing as 50, 75, 92...
        // depending on how many times the event was redelivered).
        var existingLeaveTypeIds = await _dbContext.LeaveBalances
            .Where(b => b.CompanyId == integrationEvent.CompanyId
                     && b.EmployeeId == integrationEvent.EmployeeId
                     && b.PolicyYear == policyYear)
            .Select(b => b.LeaveTypeId)
            .ToListAsync(cancellationToken);

        // Entitlement is pro-rated for employees whose start date falls after the first day of
        // the company's leave year (LeaveEntitlementCalculator is the single source of truth for
        // this, also reused by RecalculateEntitlementOnStartDateChange when a start date is later
        // corrected). Employees starting on or before the leave year start get full entitlement.
        var balances = activeLeaveTypes
            .Where(lt => !existingLeaveTypeIds.Contains(lt.Id))
            .Select(lt => LeaveBalance.Create(
                Guid.NewGuid(),
                integrationEvent.CompanyId,
                integrationEvent.EmployeeId,
                lt.Id,
                assignment.LeavePolicyId,
                policyYear,
                lt.Behaviour == LeaveTypeBehaviour.Toil
                    ? 0
                    : LeaveEntitlementCalculator.CalculateEntitlement(
                        lt.DefaultEntitlementDays, policyYearStart, policyYearEnd, integrationEvent.StartDate),
                now)).ToList();

        if (balances.Count > 0)
        {
            _dbContext.LeaveBalances.AddRange(balances);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
