namespace HR.Modules.Identity.Features.QueueInvitationBatch;

internal sealed record QueueInvitationBatchRequest(
    Guid CompanyId,
    List<Guid> EmployeeIds,
    // Populated by the endpoint from the optional "Idempotency-Key" request header, same pattern as
    // InviteEmployeeUserRequest. Null when the caller didn't supply one — see InvitationBatch's
    // IdempotencyKey remarks for the resulting, deliberately accepted, caller risk.
    string? IdempotencyKey = null);
