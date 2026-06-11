namespace HR.Modules.Leave.Features.GetLeavePolicy;

internal sealed record GetLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    DateTimeOffset CreatedAt);
