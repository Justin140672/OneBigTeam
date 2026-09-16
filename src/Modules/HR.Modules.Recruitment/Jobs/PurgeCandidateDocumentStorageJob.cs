using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Ticket 7 (P2) / Ticket 13 (P2): deletes a purged candidate's uploaded document from blob
/// storage, driven by the durable <see cref="CandidateDocumentDeletionOperation"/> persisted in the
/// SAME transaction as the CandidateDocument row deletion and candidate redaction (see
/// Features/PurgeEligibleCandidates/Handler.cs) — the operation row, not this job's own enqueue, is
/// what makes the deletion recoverable after a crash; see
/// Jobs/PurgeCandidateDocumentStorageReconciliationJob.cs for the recurring sweep that repairs a
/// missed/failed enqueue or an interrupted attempt.
///
/// Idempotent: re-checks the operation's own status before attempting a delete (a Completed
/// operation is a no-op), and storage deletion of an already-deleted/nonexistent key is treated as
/// success — so a duplicate worker/enqueue (this job's own retry, and the reconciliation sweep
/// racing it) can never fail or double-report a failure for work already done.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600, 1800 })]
internal sealed class PurgeCandidateDocumentStorageJob(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IClock clock,
    ILogger<PurgeCandidateDocumentStorageJob> logger)
{
    public const int MaxAttempts = 5;

    public async Task ProcessAsync(Guid operationId)
    {
        var operation = await db.CandidateDocumentDeletionOperations
            .SingleOrDefaultAsync(o => o.Id == operationId);

        if (operation is null)
        {
            logger.LogWarning(
                "PurgeCandidateDocumentStorageJob: no deletion operation found for id {OperationId} — skipping.",
                operationId);
            return;
        }

        if (operation.Status == CandidateDocumentDeletionOperation.StatusCompleted)
            return;

        var now = clock.UtcNowOffset();
        operation.MarkProcessing(now);
        await db.SaveChangesAsync();

        try
        {
            await storage.DeleteAsync(operation.StorageKey, CancellationToken.None);

            operation.MarkCompleted(clock.UtcNowOffset());
            await db.SaveChangesAsync();

            logger.LogInformation(
                "PurgeCandidateDocumentStorageJob: deleted storage key for operation {OperationId} (candidate {CandidateId}, company {CompanyId}).",
                operation.Id, operation.CandidateId, operation.CompanyId);
        }
        catch (Exception ex)
        {
            var isFinalAttempt = operation.AttemptCount >= MaxAttempts;

            if (isFinalAttempt)
            {
                operation.MarkFailed(ex.Message, clock.UtcNowOffset());
                await db.SaveChangesAsync();

                logger.LogError(ex,
                    "PurgeCandidateDocumentStorageJob: permanently failed to delete storage key for operation {OperationId} (candidate {CandidateId}, company {CompanyId}) after {Attempts} attempts — retained for manual remediation.",
                    operation.Id, operation.CandidateId, operation.CompanyId, MaxAttempts);
            }
            else
            {
                logger.LogWarning(ex,
                    "PurgeCandidateDocumentStorageJob: attempt {AttemptCount} failed for operation {OperationId} — will retry.",
                    operation.AttemptCount, operation.Id);
            }

            throw;
        }
    }
}
