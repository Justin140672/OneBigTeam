namespace HR.Modules.Identity.Features.RetryInvitationBatch;

internal sealed record RetryInvitationBatchRequest(Guid CompanyId, Guid BatchId);
