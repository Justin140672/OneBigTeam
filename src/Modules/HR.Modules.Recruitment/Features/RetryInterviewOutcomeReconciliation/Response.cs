namespace HR.Modules.Recruitment.Features.RetryInterviewOutcomeReconciliation;

internal sealed record RetryInterviewOutcomeReconciliationResponse(
    Guid ReconciliationId,
    Guid InterviewId,
    string Status,
    bool WasBlocked,
    bool TasksCompletionReset,
    Guid? RecoveryActionId = null,
    int RepairCount = 0);
