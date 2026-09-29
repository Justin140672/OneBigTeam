namespace HR.Modules.Sickness.Features.ListAttendanceAlerts;

internal sealed record ListAttendanceAlertsResponse(IReadOnlyList<AttendanceAlertItem> Items);

internal sealed record AttendanceAlertItem(
    Guid AlertId,
    Guid EmployeeId,
    string Rule,
    int OccurrenceCount,
    DateOnly? EvidencePeriodStart,
    DateOnly? EvidencePeriodEnd,
    string? Description,
    DateTimeOffset CreatedAt);
