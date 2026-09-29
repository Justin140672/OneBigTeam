namespace HR.Modules.Leave.Features.GetLeaveBalanceHistory;

internal sealed record GetLeaveBalanceHistoryResponse(
    Guid EmployeeId,
    Guid LeaveTypeId,
    IReadOnlyList<LeaveBalanceHistoryItem> Items);

internal sealed record LeaveBalanceHistoryItem(
    string Category,
    DateTimeOffset Date,
    string LeaveTypeName,
    decimal Change,
    string Reason,
    decimal BalanceAfter,
    string CreatedBy,
    string Description);
