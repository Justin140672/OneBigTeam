using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.RecalculateEntitlementOnLeavingDateChange;

internal sealed class LeavingDateChangeHandler(
    LeaveDbContext dbContext,
    IClock clock,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IEmployeeStartDateReader startDateReader)
    : IIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>,
      IIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>
{
    public Task HandleAsync(EmployeeLeavingDateSetIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        RecalculateAsync(
            integrationEvent.CompanyId,
            integrationEvent.EmployeeId,
            integrationEvent.LeavingDate,
            cancellationToken);

    public Task HandleAsync(EmployeeLeavingProcessCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        RecalculateAsync(
            integrationEvent.CompanyId,
            integrationEvent.EmployeeId,
            leavingDate: null,
            cancellationToken);

    private async Task RecalculateAsync(
        Guid companyId,
        Guid employeeId,
        DateOnly? leavingDate,
        CancellationToken cancellationToken)
    {
        var startDate = await startDateReader.GetStartDateAsync(companyId, employeeId, cancellationToken);

        if (startDate is null)
            return;

        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(companyId, cancellationToken);
        var now = clock.UtcNowOffset();

        var targetDate = leavingDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        var policyYear = LeaveYearCalculator.GetPolicyYear(targetDate, leaveSettings.LeaveYearStartMonth);
        var (policyYearStart, policyYearEnd) = LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);

        var balances = await dbContext.LeaveBalances
            .Where(b => b.CompanyId == companyId
                     && b.EmployeeId == employeeId
                     && b.PolicyYear == policyYear)
            .ToListAsync(cancellationToken);

        if (balances.Count == 0)
            return;

        var leaveTypesById = await dbContext.LeaveTypes
            .Where(lt => lt.CompanyId == companyId && balances.Select(b => b.LeaveTypeId).Contains(lt.Id))
            .ToDictionaryAsync(lt => lt.Id, cancellationToken);

        var changed = false;

        foreach (var balance in balances)
        {
            if (!leaveTypesById.TryGetValue(balance.LeaveTypeId, out var leaveType))
                continue;

            if (leaveType.Behaviour == LeaveTypeBehaviour.Toil)
                continue;

            var recalculated = LeaveEntitlementCalculator.CalculateEntitlement(
                leaveType.DefaultEntitlementDays, policyYearStart, policyYearEnd, startDate.Value, leavingDate);

            var recalculatedAccrualStartDate = startDate.Value < policyYearStart ? policyYearStart : startDate.Value;

            if (recalculated != balance.EntitlementDays || recalculatedAccrualStartDate != balance.AccrualStartDate)
            {
                balance.RecalculateEntitlement(recalculated, recalculatedAccrualStartDate, now);
                changed = true;
            }
        }

        if (changed)
            await dbContext.SaveChangesAsync(cancellationToken);
    }
}
