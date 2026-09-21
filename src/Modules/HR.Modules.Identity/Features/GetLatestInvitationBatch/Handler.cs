using HR.Modules.Identity.Features.GetInvitationBatchStatus;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.GetLatestInvitationBatch;

internal sealed class GetLatestInvitationBatchHandler(IdentityDbContext db)
{
    public async Task<Result<InvitationBatchStatusResponse>> HandleAsync(
        GetLatestInvitationBatchRequest request,
        CancellationToken cancellationToken)
    {
        var batch = await db.InvitationBatches
            .AsNoTracking()
            .Where(b => b.CompanyId == request.CompanyId)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (batch is null)
            return Result.Failure<InvitationBatchStatusResponse>(
                Error.NotFound("No invitation batches exist for this company yet."));

        var recipients = await db.InvitationBatchRecipients
            .AsNoTracking()
            .Where(r => r.BatchId == batch.Id)
            .ToListAsync(cancellationToken);

        return Result.Success(GetInvitationBatchStatusHandler.Map(batch, recipients));
    }
}
