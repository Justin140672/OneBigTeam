namespace HR.Modules.Leave.Domain;

internal sealed class LeaveBalance
{
    private LeaveBalance() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public Guid LeavePolicyId { get; private set; }
    public int PolicyYear { get; private set; }
    public decimal EntitlementDays { get; private set; }
    public decimal UsedDays { get; private set; }
    public decimal AdjustmentDays { get; private set; }

    /// <summary>
    /// The date from which periodic accrual (Monthly/Fortnightly - see
    /// <see cref="LeaveAccrualCalculator"/>) is measured for this balance's policy year. This is
    /// the employee's actual eligible-from date for the year: the policy year start for a
    /// continuing employee, or the employee's own (later) start date for the policy year in which
    /// they joined. Persisted (rather than re-derived) so accrual pacing for a joiner's partial
    /// year remains stable and auditable even if company leave-year settings are queried at a
    /// different point in time later.
    /// </summary>
    public DateOnly AccrualStartDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal RemainingDays => EntitlementDays + AdjustmentDays - UsedDays;

    public static LeaveBalance Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leaveTypeId,
        Guid leavePolicyId,
        int policyYear,
        decimal entitlementDays,
        DateOnly accrualStartDate,
        DateTimeOffset now)
    {
        return new LeaveBalance
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeavePolicyId = leavePolicyId,
            PolicyYear = policyYear,
            EntitlementDays = entitlementDays,
            UsedDays = 0,
            AdjustmentDays = 0,
            AccrualStartDate = accrualStartDate,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Recalculates the entitlement for this balance. Used by two independent call sites:
    ///   - EmployeeDetailsCorrectedHandler (LEAVE-04), after the employee's start date is
    ///     corrected — only invoked there when the balance is still "untouched" (no usage, no
    ///     manual adjustment), since overwriting EntitlementDays for a touched balance in that
    ///     scenario would silently invalidate a value the business has already relied upon.
    ///   - The leaving-date-change handlers (LEAVE-05), whenever a leaving date is set, amended or
    ///     cancelled — invoked there regardless of usage/adjustment, because AdjustmentDays and
    ///     UsedDays are tracked completely independently of EntitlementDays (see
    ///     <see cref="RemainingDays"/>) and are never touched by this method. If recorded usage
    ///     already exceeds the newly reduced entitlement, RemainingDays legitimately goes negative;
    ///     that is surfaced as-is, never clamped, so it is visible and reported consistently
    ///     everywhere RemainingDays is read.
    /// </summary>
    public void RecalculateEntitlement(decimal entitlementDays, DateOnly accrualStartDate, DateTimeOffset now)
    {
        EntitlementDays = entitlementDays;
        AccrualStartDate = accrualStartDate;
        UpdatedAt = now;
    }

    public void Adjust(decimal adjustmentDays, DateTimeOffset now)
    {
        AdjustmentDays += adjustmentDays;
        UpdatedAt = now;
    }

    public void RecordUsage(decimal days, DateTimeOffset now)
    {
        UsedDays += days;
        UpdatedAt = now;
    }

    public void ReverseUsage(decimal days, DateTimeOffset now)
    {
        UsedDays = Math.Max(0, UsedDays - days);
        UpdatedAt = now;
    }
}
