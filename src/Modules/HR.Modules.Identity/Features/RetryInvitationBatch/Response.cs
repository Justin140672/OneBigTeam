namespace HR.Modules.Identity.Features.RetryInvitationBatch;

internal sealed record RetryInvitationBatchResponse(Guid BatchId, int RetryCount);
