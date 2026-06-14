namespace HR.Modules.Leave.Features.ListLeaveRequests;

internal sealed record ListLeaveRequestsResponse(IReadOnlyList<LeaveRequestItem> Items);

internal sealed record LeaveRequestItem(
    Guid Id,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string Status,
    DateOnly StartDate,
    string StartPart,
    DateOnly EndDate,
    string EndPart,
    decimal TotalDays,
    string? Reason,
    DateTimeOffset CreatedAt);
