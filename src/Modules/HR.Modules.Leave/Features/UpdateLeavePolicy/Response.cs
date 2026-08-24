namespace HR.Modules.Leave.Features.UpdateLeavePolicy;

internal sealed record UpdateLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool RequiresApproval,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset UpdatedAt);
