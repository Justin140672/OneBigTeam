namespace HR.Modules.Identity.Features.QueueInvitationBatch;

internal sealed record ExcludedInvitationCandidate(Guid EmployeeId, string? Email, string Reason);

internal sealed record QueueInvitationBatchResponse(
    Guid BatchId,
    int QueuedCount,
    IReadOnlyList<ExcludedInvitationCandidate> Excluded);
