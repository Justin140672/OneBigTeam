using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Features.RecalculateEntitlementOnLeavingDateChange;

/// <summary>
/// Reacts to EmployeeLeavingDateSetIntegrationEvent (published whenever a leaving process is
/// started or amended) and EmployeeLeavingProcessCancelledIntegrationEvent (published when a
/// leaving process is cancelled) to recalculate the current policy year's entitlement for every
/// balance-tracked leave type (LEAVE-05).
///
/// This is the leaver-side mirror of EmployeeDetailsCorrectedHandler (LEAVE-04's joiner-side
/// equivalent), but with one deliberate difference: this handler recalculates balances even when
/// they already have recorded usage or a manual adjustment. EntitlementDays, UsedDays and
/// AdjustmentDays are tracked as three independent figures (see
/// <see cref="LeaveBalance.RemainingDays"/>), so overwriting EntitlementDays here never erases
/// usage or a manual adjustment — it only changes how much of the year's entitlement the employee
/// is now eligible for. Where recorded usage already exceeds the newly reduced entitlement,
/// RemainingDays legitimately goes negative; that is surfaced as-is (never clamped), which is what
/// the acceptance criteria mean by "any resulting negative remaining balance is visible and
/// reported consistently" — no separate code path is needed since RemainingDays is always computed
/// live from the three stored figures wherever it is read.
///
/// Idempotency: both events always recalculate EntitlementDays from the employee's start date and
/// (for EmployeeLeavingDateSetIntegrationEvent) their *current* LeavingDate — never as an
/// incremental delta — so repeated delivery, or a chain of Start -&gt; Amend -&gt; Amend, always
/// converges on the entitlement implied by the current leaving date rather than compounding
/// reductions. Cancellation recalculates with no leaving date at all (see
/// LeaveEntitlementCalculator), which reproduces exactly the figure the employee would have had
/// had they never entered the leaving process — no separate "snapshot to restore" needs to be
/// stored, which also means manual adjustments applied during the leaving-pending period are
/// naturally preserved (AdjustmentDays is never touched by this method).
/// </summary>
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

        // Recalculate the policy year containing the leaving date — the employee's final policy
        // year — so a future-dated leaving date always recalculates the correct year even where it
        // differs from the current one. Cancellation carries no leaving date, so it targets the
        // current policy year instead, the only year a leaver reduction could have affected.
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

            // TOIL has no default entitlement to pro-rate — its balance is built up purely through
            // awards, so a leaving-date change has nothing to recalculate here.
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
