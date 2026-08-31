namespace HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

/// <summary>
/// DSH-05 authoritative team-status counts for a manager's entire reporting sub-tree, computed
/// server-side for the current date in the company time zone.
///
/// Every count is derived from <see cref="Members"/> (which is itself already scoped to the
/// counted population), so a headline number and the drill-down list behind it can never
/// disagree — the DSH-04 explicit-query + drill-down pattern. Callers filter <see cref="Members"/>
/// by the matching flag for each tile (e.g. <c>Members.Where(m =&gt; m.OffSickToday)</c>).
/// </summary>
internal sealed record GetManagerTeamStatusSummaryResponse(
    // Active employees in the sub-tree who have started and have not left as at today.
    int TeamSize,
    // Counted, scheduled to work today, and neither on approved leave nor off sick.
    int AtWork,
    // Distinct count of members on approved leave OR off sick today (an overlapping absence
    // counts once).
    int AwayToday,
    int OnLeave,
    int Sick,
    // Members with an ACTIVE probation record (Active / ReviewDue / Extended) — not members with
    // an upcoming probation review.
    int InProbation,
    int MissingFitNotes,
    // Counted members whose working pattern says today is a non-working day (explains why
    // AtWork + AwayToday can be less than TeamSize).
    int NotScheduledToday,
    IReadOnlyList<TeamMemberStatusItem> Members);

internal sealed record TeamMemberStatusItem(
    Guid EmployeeId,
    string FullName,
    string? JobTitle,
    bool OnLeaveToday,
    bool OffSickToday,
    bool InProbation,
    bool MissingFitNote,
    bool ScheduledToday,
    // "Sick" | "OnLeave" | "NotScheduled" | "AtWork". Sick outranks OnLeave when both apply.
    string PrimaryStatus);
