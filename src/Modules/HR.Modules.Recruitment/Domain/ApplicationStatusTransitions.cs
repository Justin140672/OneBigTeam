namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Explicit, testable allowed-transition graph for <see cref="ApplicationStatus"/>, formalising what
/// was previously only implied by the guard clauses on Application's named transition methods
/// (MoveToScreening, ScheduleInterview, RecordInterviewOutcome, Offer, Hire, Reject, Withdraw).
/// Used by <see cref="Application.MoveToStage"/> (the generic transition needed for Kanban
/// drag-and-drop, which has no dedicated named method to call) to validate and apply a move.
/// </summary>
internal static class ApplicationStatusTransitions
{
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> AllowedTransitions = new()
    {
        [ApplicationStatus.Applied] =
        [
            ApplicationStatus.Screening,
            ApplicationStatus.InterviewScheduled,
            ApplicationStatus.Rejected,
            ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.Screening] =
        [
            ApplicationStatus.InterviewScheduled,
            ApplicationStatus.Rejected,
            ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.InterviewScheduled] =
        [
            ApplicationStatus.Interviewed,
            ApplicationStatus.Rejected,
            ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.Interviewed] =
        [
            ApplicationStatus.Offered,
            ApplicationStatus.Rejected,
            ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.Offered] =
        [
            ApplicationStatus.Hired,
            ApplicationStatus.Rejected,
            ApplicationStatus.Withdrawn,
        ],
        [ApplicationStatus.Hired] = [],
        [ApplicationStatus.Rejected] = [],
        [ApplicationStatus.Withdrawn] = [],
    };

    public static bool CanTransitionTo(ApplicationStatus from, ApplicationStatus to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static IReadOnlyCollection<ApplicationStatus> GetAllowedNextStages(ApplicationStatus from) =>
        AllowedTransitions.TryGetValue(from, out var allowed) ? allowed : [];
}
