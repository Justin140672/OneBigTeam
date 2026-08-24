namespace HR.Modules.Sickness.Features.ListAttendanceAlerts;

internal sealed record ListAttendanceAlertsResponse(IReadOnlyList<AttendanceAlertItem> Items);

/// <summary>
/// SICK-04 manager-view decision: HR Administrators see the full alert (rule, occurrence count,
/// evidence-window dates and the date/count-based description). Managers only get the rule type and
/// occurrence count — <see cref="EvidencePeriodStart"/>/<see cref="EvidencePeriodEnd"/>/
/// <see cref="Description"/> are null for them. Rationale: unlike "who's off today" (operational,
/// already manager-visible via GetTeamSicknessToday), an attendance-pattern alert is a clinical-
/// adjacent judgement about an individual's health-related absence history — the same sensitivity
/// posture SICK-02/03 apply to review Notes/AdjustmentDetails (HR-only). A manager still needs to
/// know *that* a report is flagged and roughly how often, so they can escalate to HR, but does not
/// need the specific dates that could be used to infer clinical detail (e.g. reconstructing exact
/// medical appointment timing).
/// </summary>
internal sealed record AttendanceAlertItem(
    Guid AlertId,
    Guid EmployeeId,
    string Rule,
    int OccurrenceCount,
    DateOnly? EvidencePeriodStart,
    DateOnly? EvidencePeriodEnd,
    string? Description,
    DateTimeOffset CreatedAt);
