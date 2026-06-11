namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed record CreateLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    DateTimeOffset CreatedAt);
