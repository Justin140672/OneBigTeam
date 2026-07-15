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
    DateTimeOffset CreatedAt,
    // Null when there's no open (Open/InProgress) leave-approval task for this request — e.g.
    // it's already been approved/rejected and its task completed, or predates task creation.
    Guid? TaskId = null);
