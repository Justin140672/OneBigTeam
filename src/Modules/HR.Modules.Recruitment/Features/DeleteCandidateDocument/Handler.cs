using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UploadCandidateDocument;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Features.DeleteCandidateDocument;

/// <summary>
/// Security/code review finding #3: the previous implementation deleted the
/// <see cref="Domain.CandidateDocument"/> row BEFORE deleting the blob — if the blob delete then
/// failed, the storage key existed nowhere else, orphaning the blob permanently with no way to
/// retry.
///
/// Fixed by reusing the SAME durable deletion mechanism as candidate purge (see
/// Features/PurgeEligibleCandidates/Handler.cs): a <see cref="CandidateDocumentDeletionOperation"/>
/// row is persisted in the SAME transaction that removes the document row, so the storage key
/// survives the document row's deletion. Only after that transaction commits is the blob delete
/// attempted directly (a latency optimisation); if it fails here, the existing
/// PurgeCandidateDocumentStorageReconciliationJob sweep retries it — no new machinery required.
/// </summary>
internal sealed class DeleteCandidateDocumentHandler(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IClock clock,
    IBackgroundJobClient backgroundJobClient,
    ILogger<DeleteCandidateDocumentHandler> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public async Task<Result> HandleAsync(
        DeleteCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var document = await db.CandidateDocuments
            .SingleOrDefaultAsync(
                cd => cd.Id == request.DocumentId &&
                      cd.CompanyId == request.CompanyId &&
                      cd.CandidateId == request.CandidateId,
                cancellationToken);

        if (document is null)
            return Result.Failure(Error.NotFound($"Candidate document '{request.DocumentId}' was not found."));

        var now = clock.UtcNowOffset();
        var storageKey = document.StorageKey;

        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), request.CompanyId, request.CandidateId, storageKey, now, executionContextAccessor?.Current);

        // Mark-for-deletion first: the deletion operation row and the document-row removal are
        // persisted in the same transaction, so the storage key is never lost even if the process
        // crashes immediately after this commit.
        db.CandidateDocumentDeletionOperations.Add(operation);
        db.CandidateDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
            operation.MarkCompleted(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort inline delete failed — the durable operation row already exists, so the
            // recurring PurgeCandidateDocumentStorageReconciliationJob sweep will retry it. Enqueue
            // a direct attempt too as a latency optimisation only.
            logger.LogWarning(ex,
                "DeleteCandidateDocumentHandler: inline blob delete failed for operation {OperationId} (company {CompanyId}, candidate {CandidateId}, storage key suffix {StorageKeySuffix}); left pending for the reconciliation sweep to retry.",
                operation.Id, request.CompanyId, request.CandidateId,
                UploadCandidateDocumentHandler.RedactStorageKey(storageKey));

            backgroundJobClient.Enqueue<PurgeCandidateDocumentStorageJob>(job => job.ProcessAsync(operation.Id));
        }

        return Result.Success();
    }
}
