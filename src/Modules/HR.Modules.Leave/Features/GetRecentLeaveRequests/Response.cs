namespace HR.Modules.Leave.Features.GetRecentLeaveRequests;

internal sealed record GetRecentLeaveRequestsResponse(IReadOnlyList<RecentLeaveRequestItem> Items);

internal sealed record RecentLeaveRequestItem(
    Guid LeaveRequestId,
    Guid EmployeeId,
    string EmployeeName,
    string LeaveTypeName,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    DateTimeOffset CreatedAt);
