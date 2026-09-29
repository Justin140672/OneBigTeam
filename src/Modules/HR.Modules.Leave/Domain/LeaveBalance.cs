using HR.SharedKernel;

namespace HR.Modules.Leave.Domain;

internal sealed class LeaveBalance : IVersionedAggregate
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

    public DateOnly AccrualStartDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // P1 #4 (optimistic concurrency): explicit, persisted concurrency token. Mapped as an EF
    // concurrency token in LeaveBalanceConfiguration. Balance mutations (RecordUsage/ReverseUsage/
    // Adjust) are read-modify-write in memory (UsedDays += days), so without a concurrency token two
    // concurrent approvals/rejections/TOIL consumptions reading the same starting balance could
    // silently lose one deduction. Every writer relies on the shared
    // VersionAdvancingSaveChangesInterceptor (wired via LeaveDbContext's UseVersionedAggregates())
    // to advance Version automatically and on EF's built-in concurrency-token check (which compares
    // the tracked entity's originally-loaded Version against the current row) to reject a stale
    // save with DbUpdateConcurrencyException — see ApproveLeaveRequestHandler, RejectLeaveRequest
    // Handler, CancelLeaveRequestHandler and ToilLedgerService.
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

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
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

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
