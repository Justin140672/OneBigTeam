namespace HR.Modules.Identity.Features.GetInvitationBatchStatus;

internal sealed record GetInvitationBatchStatusRequest(Guid CompanyId, Guid BatchId);
