using HR.Modules.Leave.Domain;

namespace HR.Modules.Leave.Features.SubmitLeaveRequestDraft;

internal sealed record SubmitDraftConflictWarning(
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

internal sealed record SubmitDraftExcludedPublicHolidayItem(DateOnly Date, string Name);

internal sealed record SubmitLeaveRequestDraftResponse(
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
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SubmitDraftConflictWarning> Conflicts,
    IReadOnlyList<SubmitDraftExcludedPublicHolidayItem> ExcludedPublicHolidays);
