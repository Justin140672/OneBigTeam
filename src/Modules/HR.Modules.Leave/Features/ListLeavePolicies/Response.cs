namespace HR.Modules.Leave.Features.ListLeavePolicies;

internal sealed record ListLeavePoliciesResponse(IReadOnlyList<LeavePolicyItem> Items);

internal sealed record LeavePolicyItem(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool RequiresApproval,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset CreatedAt);
