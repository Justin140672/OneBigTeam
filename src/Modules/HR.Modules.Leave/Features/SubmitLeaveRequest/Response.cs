using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.SubmitLeaveRequest;

internal sealed record LeaveConflictWarning(
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

internal sealed record SubmitExcludedPublicHolidayItem(DateOnly Date, string Name);

internal sealed record SubmitLeaveRequestResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid? LeavePolicyId,
    string Status,
    DateOnly StartDate,
    LeaveDayPart StartPart,
    DateOnly EndDate,
    LeaveDayPart EndPart,
    decimal TotalDays,
    string? Reason,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LeaveConflictWarning> Conflicts,
    IReadOnlyList<SubmitExcludedPublicHolidayItem> ExcludedPublicHolidays);
