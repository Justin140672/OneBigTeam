namespace HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

internal sealed record GetManagerTeamStatusSummaryResponse(
    int TeamSize,
    int AtWork,
    int AwayToday,
    int OnLeave,
    int Sick,
    int InProbation,
    int MissingFitNotes,
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
    string PrimaryStatus);
