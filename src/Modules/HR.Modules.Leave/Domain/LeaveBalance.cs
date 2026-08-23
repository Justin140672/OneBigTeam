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
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Recalculates the entitlement for this balance (e.g. after the employee's start date is
    /// corrected). Callers must only invoke this when the balance is still "untouched" — i.e. no
    /// usage recorded and no manual adjustment applied — since AdjustmentDays/UsedDays already
    /// preserve any manual changes or leave transactions separately from EntitlementDays and must
    /// never be silently overwritten by a recalculation.
    /// </summary>
    public void RecalculateEntitlement(decimal entitlementDays, DateTimeOffset now)
    {
        EntitlementDays = entitlementDays;
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
