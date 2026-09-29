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

        // Internal recruitment Ticket 1: a CV recorded as submitted with an application is part of
        // that application's history and must not disappear from under it. The database enforces
        // this too (ON DELETE RESTRICT); this pre-check turns it into a clear 409 instead of a 500.
        var referencingApplications = await db.Applications
            .CountAsync(a => a.CompanyId == request.CompanyId && a.CvDocumentId == document.Id, cancellationToken);

        if (referencingApplications > 0)
            return Result.Failure(ReferencedByApplicationsConflict(referencingApplications));

        var now = clock.UtcNowOffset();
        var storageKey = document.StorageKey;

        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), request.CompanyId, request.CandidateId, storageKey, now, executionContextAccessor?.Current);

        db.CandidateDocumentDeletionOperations.Add(operation);
        db.CandidateDocuments.Remove(document);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            // Race: an application started referencing this CV between the pre-check above and the
            // delete. The FK rejected the delete, so nothing (including the deletion operation row)
            // was committed; the blob is untouched.
            db.ChangeTracker.Clear();
            return Result.Failure(ReferencedByApplicationsConflict(null));
        }

        try
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
            operation.MarkCompleted(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "DeleteCandidateDocumentHandler: inline blob delete failed for operation {OperationId} (company {CompanyId}, candidate {CandidateId}, storage key suffix {StorageKeySuffix}); left pending for the reconciliation sweep to retry.",
                operation.Id, request.CompanyId, request.CandidateId,
                UploadCandidateDocumentHandler.RedactStorageKey(storageKey));

            backgroundJobClient.Enqueue<PurgeCandidateDocumentStorageJob>(job => job.ProcessAsync(operation.Id));
        }

        return Result.Success();
    }

    private static Error ReferencedByApplicationsConflict(int? applicationCount) => Error.Conflict(
        applicationCount is int count
            ? $"This CV is recorded as the submitted CV on {count} application(s). Change or remove the CV on those applications before deleting it."
            : "This CV is recorded as the submitted CV on an application. Change or remove the CV on that application before deleting it.");

    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.ForeignKeyViolation };
}
