namespace HR.Modules.Leave.Features.ListLeaveTypes;

internal sealed record ListLeaveTypesResponse(IReadOnlyList<LeaveTypeItem> Items);

internal sealed record LeaveTypeItem(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Code,
    int DefaultEntitlementDays,
    string AccrualMethod,
    string Behaviour,
    bool IsActive,
    bool HasBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
