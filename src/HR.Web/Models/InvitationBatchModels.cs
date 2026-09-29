namespace HR.Web.Models;

public record QueueInvitationBatchRequest(List<Guid> EmployeeIds);

public record QueueInvitationBatchResponse(
    Guid BatchId,
    int QueuedCount,
    List<InvitationBatchExcludedEmployeeModel> Excluded);

public record InvitationBatchExcludedEmployeeModel(
    Guid EmployeeId,
    string? Email,
    string Reason);

public record InvitationBatchStatusResponse(
    Guid BatchId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    InvitationBatchCountsModel Counts,
    List<InvitationBatchRecipientModel> Recipients);

public record InvitationBatchCountsModel(
    int Waiting,
    int Processing,
    int Sent,
    int Skipped,
    int Failed);

public record InvitationBatchRecipientModel(
    Guid EmployeeId,
    string Email,
    string Status,
    string? FailureReason,
    DateTimeOffset? ProcessedAt);

public record InvitationRecipientCandidate(Guid EmployeeId, string Name, string Email);

public record InvitationExclusionCandidate(string Name, string? Email, string Reason);
