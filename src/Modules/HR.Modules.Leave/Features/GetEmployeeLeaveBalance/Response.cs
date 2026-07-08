namespace HR.Modules.Leave.Features.GetEmployeeLeaveBalance;

internal sealed record GetEmployeeLeaveBalanceResponse(
    Guid EmployeeId,
    int PolicyYear,
    IReadOnlyList<LeaveBalanceItem> Balances);

/// <summary>
/// Represents one leave type row for the employee. When <see cref="HasBalance"/> is false, the
/// employee has no <c>LeaveBalance</c> row for this type/policy year (e.g. an Unpaid Leave type
/// that is never tracked with an entitlement) and all balance/hours fields are null — the UI
/// should render this as "n/a" with no Adjust action.
/// </summary>
internal sealed record LeaveBalanceItem(
    Guid? LeaveBalanceId,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeCode,
    bool HasBalance,
    decimal? EntitlementDays,
    decimal? UsedDays,
    decimal? AdjustmentDays,
    decimal? RemainingDays,
    decimal PendingDays,
    decimal? EntitlementHours,
    decimal? RemainingHours,
    decimal PendingHours);
