using Hangfire;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.RetryInvitationBatch;

internal sealed class RetryInvitationBatchHandler(
    IdentityDbContext db,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<RetryInvitationBatchResponse>> HandleAsync(
        RetryInvitationBatchRequest request,
        CancellationToken cancellationToken)
    {
        var batch = await db.InvitationBatches
            .SingleOrDefaultAsync(b => b.Id == request.BatchId, cancellationToken);

        if (batch is null || batch.CompanyId != request.CompanyId)
            return Result.Failure<RetryInvitationBatchResponse>(
                Error.NotFound("Invitation batch was not found in this company."));

        var failedRecipients = await db.InvitationBatchRecipients
            .Where(r => r.BatchId == batch.Id && r.Status == InvitationBatchRecipient.StatusFailed)
            .ToListAsync(cancellationToken);

        if (failedRecipients.Count == 0)
            return Result.Failure<RetryInvitationBatchResponse>(
                Error.Validation("Nothing to retry — this batch has no failed recipients."));

        foreach (var recipient in failedRecipients)
        {
            recipient.ResetForRetry();
        }

        // A batch only settles to Completed once ProcessInvitationBatchJob's loop finishes — reopen
        // it so the retry pass is visible while it runs. Sent/Skipped recipients are left untouched
        // (never re-processed — see InvitationBatchRecipient.ResetForRetry remarks).
        batch.ReopenForRetry();

        await db.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<ProcessInvitationBatchJob>(job => job.RunAsync(batch.Id, CancellationToken.None));

        return Result.Success(new RetryInvitationBatchResponse(batch.Id, failedRecipients.Count));
    }
}
