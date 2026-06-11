namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed record AssignLeavePolicyToEmployeeRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid LeavePolicyId { get; init; }
    public DateOnly EffectiveFrom { get; init; }
}
