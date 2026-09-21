using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.GetInvitationBatchStatus;

internal sealed class GetInvitationBatchStatusHandler(IdentityDbContext db)
{
    public async Task<Result<InvitationBatchStatusResponse>> HandleAsync(
        GetInvitationBatchStatusRequest request,
        CancellationToken cancellationToken)
    {
        var batch = await db.InvitationBatches
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == request.BatchId, cancellationToken);

        if (batch is null || batch.CompanyId != request.CompanyId)
            return Result.Failure<InvitationBatchStatusResponse>(
                Error.NotFound("Invitation batch was not found in this company."));

        var recipients = await db.InvitationBatchRecipients
            .AsNoTracking()
            .Where(r => r.BatchId == batch.Id)
            .ToListAsync(cancellationToken);

        return Result.Success(Map(batch, recipients));
    }

    internal static InvitationBatchStatusResponse Map(InvitationBatch batch, List<InvitationBatchRecipient> recipients)
    {
        var counts = new InvitationBatchRecipientCounts(
            Waiting: recipients.Count(r => r.Status == InvitationBatchRecipient.StatusWaiting),
            Processing: recipients.Count(r => r.Status == InvitationBatchRecipient.StatusProcessing),
            Sent: recipients.Count(r => r.Status == InvitationBatchRecipient.StatusSent),
            Skipped: recipients.Count(r => r.Status == InvitationBatchRecipient.StatusSkipped),
            Failed: recipients.Count(r => r.Status == InvitationBatchRecipient.StatusFailed));

        var recipientResults = recipients
            .Select(r => new InvitationBatchRecipientResult(r.EmployeeId, r.Email, r.Status, r.FailureReason, r.ProcessedAt))
            .ToList();

        return new InvitationBatchStatusResponse(
            batch.Id, batch.Status, batch.CreatedAt, batch.StartedAt, batch.CompletedAt, counts, recipientResults);
    }
}
