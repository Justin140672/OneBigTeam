using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Core, per-company leave-year rollover logic (LEAVE-03). Creates the new policy-year
/// <see cref="LeaveBalance"/> for every employee/leave-type combination that had a balance in the
/// previous policy year AND still has an active <see cref="EmployeeLeavePolicyAssignment"/>,
/// carrying forward unused entitlement up to the employee's current leave policy's
/// <see cref="LeavePolicy.CarryOverDays"/> limit. Negative balances are never carried forward.
/// Employees whose assignment has been deactivated (their departure was finalised — see
/// EmployeeDepartureFinalisedHandler) are skipped entirely: they keep their historical balances
/// but do not receive a new policy-year balance or carry-over.
///
/// Idempotent by design: before creating anything it checks which (EmployeeId, LeaveTypeId) pairs
/// already have a balance for the target policy year and skips them. The underlying
/// leave_balances table also enforces a unique (company_id, employee_id, leave_type_id,
/// policy_year) index (see LeaveBalanceConfiguration) as a defence-in-depth backstop against a
/// concurrent duplicate insert. Combined, a caller can safely re-run or retry this method for the
/// same company/policy year with no further changes once it has completed successfully.
///
/// Intentionally separate from <see cref="LeaveYearRolloverJob"/> (which decides *when* a company's
/// rollover day has arrived) so this method can be invoked directly and deterministically in tests
/// without needing to fake the current date/time zone.
/// </summary>
internal sealed class LeaveYearRolloverService(
    LeaveDbContext dbContext,
    IClock clock,
    ICompanyLeaveSettingsReader leaveSettingsReader,
    IAuditEventPublisher auditPublisher)
{
    // Background-job actor convention used elsewhere in the codebase (e.g.
    // ProcessDocumentExpiryNotifications, FitNoteRequestJob) for adjustments/events with no human
    // actor.
    internal static readonly Guid SystemActorId = Guid.Empty;

    public async Task<LeaveYearRolloverResult> RolloverCompanyAsync(
        Guid companyId,
        int newPolicyYear,
        CancellationToken cancellationToken)
    {
        var previousPolicyYear = newPolicyYear - 1;
        var now = clock.UtcNowOffset();

        // New policy-year balances start accruing (for Monthly/Fortnightly leave types - LEAVE-04)
        // from the new policy year's start date - the employee is a continuing employee by
        // definition here (a departed employee's assignment was deactivated and is filtered out
        // below), so there is no partial-year pro-rating to account for.
        var leaveSettings = await leaveSettingsReader.GetLeaveSettingsAsync(companyId, cancellationToken);
        var (newPolicyYearStart, _) = LeaveYearCalculator.GetPolicyYearBounds(newPolicyYear, leaveSettings.LeaveYearStartMonth);

        var previousBalances = await dbContext.LeaveBalances
            .Where(b => b.CompanyId == companyId && b.PolicyYear == previousPolicyYear)
            .ToListAsync(cancellationToken);

        if (previousBalances.Count == 0)
            return LeaveYearRolloverResult.Empty;

        var leaveTypeIds = previousBalances.Select(b => b.LeaveTypeId).Distinct().ToList();
        var leaveTypes = await dbContext.LeaveTypes
            .Where(lt => leaveTypeIds.Contains(lt.Id))
            .ToDictionaryAsync(lt => lt.Id, cancellationToken);

        var policyIds = previousBalances.Select(b => b.LeavePolicyId).Distinct().ToList();
        var employeeIds = previousBalances.Select(b => b.EmployeeId).Distinct().ToList();

        // Employees may have been reassigned to a different policy since the previous policy
        // year — the *current* assignment governs the new year's carry-over limit. Only employees
        // with an active assignment are eligible: a departed employee's assignment is deactivated
        // (not deleted) by EmployeeDepartureFinalisedHandler when their leaving process is
        // finalised, and rollover must not generate a new policy-year balance/carry-over for them
        // — see EmployeeDepartureFinalisedHandler for the full rationale. Employees who still have
        // an active assignment but none was found are treated the same way as an inactive
        // assignment (skip) rather than silently falling back to the previous balance's policy,
        // since "no active assignment" is itself the signal that the employee is no longer
        // eligible for rollover.
        var assignments = (await dbContext.EmployeeLeavePolicyAssignments
                .Where(a => a.CompanyId == companyId && employeeIds.Contains(a.EmployeeId) && a.IsActive)
                .ToListAsync(cancellationToken))
            .ToDictionary(a => a.EmployeeId);

        var assignmentPolicyIds = assignments.Values.Select(a => a.LeavePolicyId);
        var allPolicyIds = policyIds.Union(assignmentPolicyIds).Distinct().ToList();
        var policies = await dbContext.LeavePolicies
            .Where(p => allPolicyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var existingNewYearKeys = (await dbContext.LeaveBalances
                .Where(b => b.CompanyId == companyId && b.PolicyYear == newPolicyYear)
                .Select(b => new { b.EmployeeId, b.LeaveTypeId })
                .ToListAsync(cancellationToken))
            .Select(x => (x.EmployeeId, x.LeaveTypeId))
            .ToHashSet();

        var newBalances = new List<LeaveBalance>();
        var carryOvers = new List<(LeaveBalanceAdjustment Adjustment, LeaveBalance Balance)>();

        foreach (var previous in previousBalances)
        {
            // Idempotency guard — a prior (possibly interrupted) run may already have created this
            // employee/leave-type's balance for the new policy year.
            if (existingNewYearKeys.Contains((previous.EmployeeId, previous.LeaveTypeId)))
                continue;

            if (!leaveTypes.TryGetValue(previous.LeaveTypeId, out var leaveType) ||
                !leaveType.IsActive ||
                !leaveType.HasBalance)
                continue;

            // No active policy assignment (never assigned, or the employee's departure has been
            // finalised and their assignment was deactivated) — do not generate a new policy-year
            // balance or carry-over for them.
            if (!assignments.TryGetValue(previous.EmployeeId, out var assignment))
                continue;

            var leavePolicyId = assignment.LeavePolicyId;

            if (!policies.TryGetValue(leavePolicyId, out var policy))
                continue;

            var baseEntitlement = leaveType.Behaviour == LeaveTypeBehaviour.Toil
                ? 0m
                : leaveType.DefaultEntitlementDays;

            var newBalance = LeaveBalance.Create(
                Guid.NewGuid(),
                companyId,
                previous.EmployeeId,
                previous.LeaveTypeId,
                leavePolicyId,
                newPolicyYear,
                baseEntitlement,
                newPolicyYearStart,
                now);

            // Never carry a negative balance forward — an exhausted/over-drawn balance simply
            // carries zero. A CarryOverDays limit of zero also carries nothing.
            var remaining = previous.RemainingDays;
            var carryOverDays = remaining > 0 ? Math.Min(remaining, policy.CarryOverDays) : 0m;

            if (carryOverDays > 0)
            {
                newBalance.Adjust(carryOverDays, now);

                var adjustment = LeaveBalanceAdjustment.Create(
                    Guid.NewGuid(),
                    companyId,
                    previous.EmployeeId,
                    previous.LeaveTypeId,
                    carryOverDays,
                    null,
                    LeaveBalanceAdjustmentReason.CarryOver,
                    $"From policy year {previousPolicyYear}",
                    SystemActorId,
                    now);

                carryOvers.Add((adjustment, newBalance));
            }

            newBalances.Add(newBalance);
        }

        if (newBalances.Count == 0)
            return LeaveYearRolloverResult.Empty;

        dbContext.LeaveBalances.AddRange(newBalances);
        dbContext.LeaveBalanceAdjustments.AddRange(carryOvers.Select(x => x.Adjustment));

        // Explicit transaction so the new balances and their carry-over adjustments are committed
        // atomically — a partial write would otherwise leave a balance with no matching adjustment
        // record for its carried-over days.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var (adjustment, balance) in carryOvers)
        {
            await auditPublisher.PublishAsync(new LeaveBalanceAdjustedAuditEvent(
                companyId,
                balance.EmployeeId,
                balance.LeaveTypeId,
                balance.Id,
                newPolicyYear,
                adjustment.AdjustmentDays,
                balance.RemainingDays,
                SystemActorId,
                now,
                AdjustmentHours: null,
                Reason: adjustment.Reason.ToString()), cancellationToken);
        }

        return new LeaveYearRolloverResult(newBalances.Count, carryOvers.Count);
    }
}

internal sealed record LeaveYearRolloverResult(int BalancesCreated, int CarryOverAdjustmentsCreated)
{
    public static LeaveYearRolloverResult Empty { get; } = new(0, 0);
}
