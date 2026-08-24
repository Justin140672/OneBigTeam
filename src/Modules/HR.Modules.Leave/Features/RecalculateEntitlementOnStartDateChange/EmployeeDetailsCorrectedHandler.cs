using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.RecalculateEntitlementOnStartDateChange;

/// <summary>
/// Reacts to EmployeeDetailsCorrectedIntegrationEvent (published whenever an employee's profile,
/// including start date, is corrected) and recalculates the current policy year's entitlement for
/// any balance that has not yet been touched — i.e. no usage recorded and no manual adjustment
/// applied. Balances with recorded usage or a manual adjustment are left untouched, since
/// overwriting EntitlementDays for those would silently invalidate a value the business has
/// already relied upon or explicitly set.
///
/// The event is deliberately generic and does not carry the new start date (see
/// EmployeeDetailsCorrectedIntegrationEvent's doc comment), so the current start date is read via
/// IEmployeeStartDateReader, a purpose-specific module-owned contract — not by querying the
/// Employees module's tables directly.
/// </summary>
internal sealed class EmployeeDetailsCorrectedHandler : IIntegrationEventHandler<EmployeeDetailsCorrectedIntegrationEvent>
{
    private readonly LeaveDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICompanyLeaveSettingsReader _leaveSettingsReader;
    private readonly IEmployeeStartDateReader _startDateReader;

    public EmployeeDetailsCorrectedHandler(
        LeaveDbContext dbContext,
        IClock clock,
        ICompanyLeaveSettingsReader leaveSettingsReader,
        IEmployeeStartDateReader startDateReader)
    {
        _dbContext = dbContext;
        _clock = clock;
        _leaveSettingsReader = leaveSettingsReader;
        _startDateReader = startDateReader;
    }

    public async Task HandleAsync(EmployeeDetailsCorrectedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var startDate = await _startDateReader.GetStartDateAsync(
            integrationEvent.CompanyId, integrationEvent.EmployeeId, cancellationToken);

        if (startDate is null)
            return;

        var leaveSettings = await _leaveSettingsReader.GetLeaveSettingsAsync(integrationEvent.CompanyId, cancellationToken);
        var now = _clock.UtcNowOffset();
        var policyYear = LeaveYearCalculator.GetPolicyYear(now, leaveSettings.LeaveYearStartMonth);
        var (policyYearStart, policyYearEnd) = LeaveYearCalculator.GetPolicyYearBounds(policyYear, leaveSettings.LeaveYearStartMonth);

        var balances = await _dbContext.LeaveBalances
            .Where(b => b.CompanyId == integrationEvent.CompanyId
                     && b.EmployeeId == integrationEvent.EmployeeId
                     && b.PolicyYear == policyYear)
            .ToListAsync(cancellationToken);

        if (balances.Count == 0)
            return;

        var leaveTypesById = await _dbContext.LeaveTypes
            .Where(lt => lt.CompanyId == integrationEvent.CompanyId
                      && balances.Select(b => b.LeaveTypeId).Contains(lt.Id))
            .ToDictionaryAsync(lt => lt.Id, cancellationToken);

        var changed = false;

        foreach (var balance in balances)
        {
            // Safety guard: never overwrite a balance that has recorded usage or a manual
            // adjustment — those signal the balance is no longer "untouched" and recalculating
            // EntitlementDays underneath them could silently invalidate a value the business has
            // already relied upon.
            if (balance.UsedDays != 0 || balance.AdjustmentDays != 0)
                continue;

            if (!leaveTypesById.TryGetValue(balance.LeaveTypeId, out var leaveType))
                continue;

            if (leaveType.Behaviour == LeaveTypeBehaviour.Toil)
                continue;

            var recalculated = LeaveEntitlementCalculator.CalculateEntitlement(
                leaveType.DefaultEntitlementDays, policyYearStart, policyYearEnd, startDate.Value);

            var recalculatedAccrualStartDate = startDate.Value < policyYearStart ? policyYearStart : startDate.Value;

            if (recalculated != balance.EntitlementDays || recalculatedAccrualStartDate != balance.AccrualStartDate)
            {
                balance.RecalculateEntitlement(recalculated, recalculatedAccrualStartDate, now);
                changed = true;
            }
        }

        if (changed)
            await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
