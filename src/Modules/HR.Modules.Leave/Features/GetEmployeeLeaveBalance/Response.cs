namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed record GetEmployeeLeaveBalanceResponse(
    Guid EmployeeId,
    int PolicyYear,
    IReadOnlyList<LeaveBalanceItem> Balances);

internal sealed record LeaveBalanceItem(
    Guid? LeaveBalanceId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    bool HasBalance,
    decimal? EntitlementDays,
    decimal? AccruedDays,
    decimal? UsedDays,
    decimal? AdjustmentDays,
    decimal? RemainingDays,
    decimal PendingDays,
    decimal? EntitlementHours,
    decimal? RemainingHours,
    decimal PendingHours);
