namespace HR.Modules.Recruitment.Features.ListBlockedInterviewOutcomeReconciliations;

internal sealed record BlockedInterviewOutcomeReconciliationItem(
    Guid ReconciliationId,
    Guid InterviewId,
    Guid ApplicationId,
    Guid? TaskId,
    Guid? TasksOperationId,
    string? Category,
    string? FailureReason,
    int AttemptCount,
    DateTimeOffset BlockedAt);

internal sealed record ListBlockedInterviewOutcomeReconciliationsResponse(
    IReadOnlyList<BlockedInterviewOutcomeReconciliationItem> Items);
