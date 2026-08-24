namespace HR.Modules.Leave.Domain;

internal sealed class EmployeeLeavePolicyAssignment
{
    private EmployeeLeavePolicyAssignment() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeavePolicyId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmployeeLeavePolicyAssignment Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        Guid leavePolicyId,
        DateOnly effectiveFrom,
        DateTimeOffset now)
    {
        return new EmployeeLeavePolicyAssignment
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeavePolicyId = leavePolicyId,
            EffectiveFrom = effectiveFrom,
            IsActive = true,
            DeactivatedAt = null,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(Guid leavePolicyId, DateOnly effectiveFrom, DateTimeOffset now)
    {
        LeavePolicyId = leavePolicyId;
        EffectiveFrom = effectiveFrom;
        UpdatedAt = now;
    }

    // Called when the employee's departure has been finalised (EmployeeDepartureFinalisedIntegrationEvent).
    // The row is kept — not deleted — so historical LeaveBalance/LeaveBalanceAdjustment rows for
    // this employee still resolve a policy via LeaveYearRolloverService's dictionary lookups, and
    // so re-hiring the same person later has a clear prior record. Idempotent: reactivating is not
    // supported by this type today, and repeated Deactivate calls are harmless no-ops.
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
            return;

        IsActive = false;
        DeactivatedAt = now;
        UpdatedAt = now;
    }
}
