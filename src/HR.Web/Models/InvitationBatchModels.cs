namespace HR.Web.Models;

// ── POST /api/companies/{companyId}/invitation-batches ─────────────────────
public record QueueInvitationBatchRequest(List<Guid> EmployeeIds);

public record QueueInvitationBatchResponse(
    Guid BatchId,
    int QueuedCount,
    List<InvitationBatchExcludedEmployeeModel> Excluded);

public record InvitationBatchExcludedEmployeeModel(
    Guid EmployeeId,
    string? Email,
    string Reason);

// ── GET /api/companies/{companyId}/invitation-batches/{batchId} ────────────
// ── GET /api/companies/{companyId}/invitation-batches/latest ───────────────
// ── POST /api/companies/{companyId}/invitation-batches/{batchId}/retry ─────
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

// ── UI-only models for the bulk-invite confirmation flow (EmployeeList.razor +
// BulkInviteConfirmDialog) — not API DTOs, just a shared shape for candidates picked either from
// the normal employee grid (EmployeeListItemModel) or the invitation-mode grid
// (InvitableEmployeeModel), so the confirmation dialog doesn't need to know which mode produced
// the selection.
public record InvitationRecipientCandidate(Guid EmployeeId, string Name, string Email);

public record InvitationExclusionCandidate(string Name, string? Email, string Reason);
