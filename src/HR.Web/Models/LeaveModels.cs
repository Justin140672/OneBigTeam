using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

public sealed record LeaveBalanceResponse(
    Guid EmployeeId,
    int PolicyYear,
    IReadOnlyList<LeaveBalanceItemModel> Balances);

public sealed record LeaveBalanceItemModel(
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

// ── Leave balance adjustment ─────────────────────────────────────────────

public enum LeaveBalanceAdjustmentReason { Correction, CarryOver, ManualAward, ManualDeduction, Other }

public sealed record AdjustLeaveBalanceModel(
    Guid LeaveTypeId,
    decimal AdjustmentValue,
    LeaveBalanceAdjustmentReason Reason,
    string? Comments,
    bool AllowNegativeOverride);

public sealed record AdjustLeaveBalanceResponse(
    Guid AdjustmentId,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeaveBalanceId,
    decimal AdjustmentDays,
    decimal? AdjustmentHours,
    decimal NewRemainingDays,
    decimal NewRemainingHours,
    string Reason,
    string? Comments,
    Guid AdjustedByEmployeeId,
    DateTimeOffset AdjustedAt);

// ── Leave balance history ────────────────────────────────────────────────

public sealed record LeaveBalanceHistoryResponse(
    Guid EmployeeId,
    Guid LeaveTypeId,
    IReadOnlyList<LeaveBalanceHistoryItemModel> Items);

/// <param name="Category">"ApprovedLeave" | "CancelledLeave" | "ToilAward" | "ManualAdjustment" | "CarryOver".</param>
/// <param name="Change">Signed hours: negative when the event consumed balance, positive when it added to it.</param>
/// <param name="Reason">Adjustment reason enum name for manual adjustments/carry-over, or a fixed label
/// ("Leave Taken"/"Leave Cancelled"/"TOIL Award") for the other categories.</param>
/// <param name="BalanceAfter">Running balance in hours immediately after this event.</param>
public sealed record LeaveBalanceHistoryItemModel(
    string Category,
    DateTimeOffset Date,
    string LeaveTypeName,
    decimal Change,
    string Reason,
    decimal BalanceAfter,
    string CreatedBy,
    string Description);

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

// ── Leave policies ─────────────────────────────────────────────────────

public sealed record ListLeavePoliciesResponse(List<LeavePolicyListItemModel> Items);

public sealed record LeavePolicyListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record GetLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record CreateLeavePolicyRequest(
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance);

public record CreateLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    DateTimeOffset CreatedAt);

public record UpdateLeavePolicyRequest(
    Guid CompanyId,
    Guid PolicyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance);

public record UpdateLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    DateTimeOffset UpdatedAt);

public sealed class LeavePolicyEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [Range(0, int.MaxValue, ErrorMessage = "Carry over days cannot be negative.")]
    public int CarryOverDays { get; set; }
    public bool AllowNegativeBalance { get; set; }
}

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
    string? RejectionReason,
    DateTimeOffset CreatedAt);

public sealed record GetLeaveRequestResponse(
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
    string? RejectionReason,
    DateTimeOffset CreatedAt);

// ── DASHBOARD: RECENT LEAVE REQUESTS ────────────────────────────────────────────

public sealed record GetRecentLeaveRequestsResponse(IReadOnlyList<RecentLeaveRequestItem> Items);

public sealed record RecentLeaveRequestItem(
    Guid LeaveRequestId,
    Guid EmployeeId,
    string EmployeeName,
    string LeaveTypeName,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalDays,
    DateTimeOffset CreatedAt);
