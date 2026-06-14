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
    decimal RemainingDays,
    decimal PendingDays);

// ── Leave request ─────────────────────────────────────────────────────

public enum LeaveDayPart { FullDay, Morning, Afternoon }

public sealed record PreviewLeaveRequestModel(
    Guid LeaveTypeId,
    DateOnly StartDate,
    LeaveDayPart StartPart,
    DateOnly EndDate,
    LeaveDayPart EndPart);

public sealed record ExcludedPublicHolidayItem(DateOnly Date, string Name);

public sealed record LeaveConflictItem(
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public sealed record PreviewLeaveResponse(
    decimal TotalDays,
    IReadOnlyList<ExcludedPublicHolidayItem> ExcludedPublicHolidays,
    IReadOnlyList<LeaveConflictItem> Conflicts,
    decimal? RemainingBalance,
    bool WouldExceedBalance);

public sealed record SubmitLeaveRequestModel(
    Guid LeaveTypeId,
    DateOnly StartDate,
    LeaveDayPart StartPart,
    DateOnly EndDate,
    LeaveDayPart EndPart,
    string? Reason);

public sealed record SubmitLeaveResponse(Guid Id, string Status, decimal TotalDays);

// ── Leave request list ─────────────────────────────────────────────────────

public sealed record LeaveRequestListResponse(IReadOnlyList<LeaveRequestListItem> Items);

public sealed record LeaveRequestListItem(
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
