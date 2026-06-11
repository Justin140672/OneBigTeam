namespace HR.Modules.Leave.Domain;

internal sealed class EmployeeLeavePolicyAssignment
{
    private EmployeeLeavePolicyAssignment() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid LeavePolicyId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
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
}
