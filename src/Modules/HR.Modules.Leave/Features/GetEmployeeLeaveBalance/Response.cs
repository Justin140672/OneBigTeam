namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed record GetEmployeeLeaveBalanceResponse(
    Guid EmployeeId,
    int PolicyYear,
    IReadOnlyList<LeaveBalanceItem> Balances);

internal sealed record LeaveBalanceItem(
    Guid LeaveBalanceId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal EntitlementDays,
    decimal UsedDays,
    decimal AdjustmentDays,
    decimal RemainingDays);
