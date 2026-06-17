namespace HR.Modules.Leave.Features.PreviewLeaveRequest;

internal sealed record ExcludedPublicHolidayItem(DateOnly Date, string Name);

internal sealed record PreviewConflict(
    Guid LeaveRequestId,
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

internal sealed record PreviewLeaveRequestResponse(
    decimal TotalDays,
    IReadOnlyList<ExcludedPublicHolidayItem> ExcludedPublicHolidays,
    IReadOnlyList<PreviewConflict> Conflicts,
    decimal? RemainingBalance,
    bool WouldExceedBalance);
