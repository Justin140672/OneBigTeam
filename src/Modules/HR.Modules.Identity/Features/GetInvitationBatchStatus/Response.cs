namespace HR.Modules.Identity.Features.GetInvitationBatchStatus;

internal sealed record InvitationBatchRecipientResult(
    Guid EmployeeId,
    string Email,
    string Status,
    string? FailureReason,
    DateTimeOffset? ProcessedAt);

internal sealed record InvitationBatchRecipientCounts(
    int Waiting,
    int Processing,
    int Sent,
    int Skipped,
    int Failed);

internal sealed record InvitationBatchStatusResponse(
    Guid BatchId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    InvitationBatchRecipientCounts Counts,
    IReadOnlyList<InvitationBatchRecipientResult> Recipients);
