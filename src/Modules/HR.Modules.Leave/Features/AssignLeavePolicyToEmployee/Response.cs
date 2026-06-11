namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed record AssignLeavePolicyToEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeavePolicyId,
    DateOnly EffectiveFrom,
    DateTimeOffset CreatedAt);
