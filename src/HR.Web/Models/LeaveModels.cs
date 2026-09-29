using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

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


public sealed record LeaveBalanceHistoryResponse(
    Guid EmployeeId,
    Guid LeaveTypeId,
    IReadOnlyList<LeaveBalanceHistoryItemModel> Items);

public sealed record LeaveBalanceHistoryItemModel(
    string Category,
    DateTimeOffset Date,
    string LeaveTypeName,
    decimal Change,
    string Reason,
    decimal BalanceAfter,
    string CreatedBy,
    string Description)
{
    public string ReasonText => HR.SharedKernel.EnumText.Humanize(Reason);
}


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


public sealed record ListLeavePoliciesResponse(List<LeavePolicyListItemModel> Items);

public sealed record LeavePolicyListItemModel(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public sealed record GetLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    int Version = 0);

public record CreateLeavePolicyRequest(
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsDefault);

public record CreateLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset CreatedAt);

public record UpdateLeavePolicyRequest(
    Guid CompanyId,
    Guid PolicyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsDefault,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateLeavePolicyResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int CarryOverDays,
    bool AllowNegativeBalance,
    bool IsActive,
    bool IsDefault,
    DateTimeOffset UpdatedAt,
    int Version = 0);

public sealed class LeavePolicyEditModel : IHasVersion
{
    public int Version { get; set; }
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [Range(0, int.MaxValue, ErrorMessage = "Carry over days cannot be negative.")]
    public int CarryOverDays { get; set; }
    public bool AllowNegativeBalance { get; set; }
    public bool IsDefault { get; set; }
}


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
    DateTimeOffset CreatedAt,
    Guid? TaskId = null);
