namespace HR.Web.Models;

public sealed record LeaveBalanceResponse(
    Guid EmployeeId,
    int PolicyYear,
    IReadOnlyList<LeaveBalanceItemModel> Balances);

public sealed record LeaveBalanceItemModel(
    Guid LeaveBalanceId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    decimal EntitlementDays,
    decimal UsedDays,
    decimal AdjustmentDays,
    decimal RemainingDays);
