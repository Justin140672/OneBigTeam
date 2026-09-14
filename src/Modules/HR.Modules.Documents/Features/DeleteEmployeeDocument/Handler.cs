using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.DeleteEmployeeDocument;

// DOC-04: "delete" now archives (soft-delete) the employee-document record instead of hard
// deleting the row and the underlying stored file. No DB row is removed here and no
// IDocumentStorageService.DeleteAsync call is made — physical file removal only ever happens via
// the separately authorised PurgeEligibleArchivedEmployeeDocuments retention process, once a
// document has been archived for the configured minimum retention period.
internal sealed class DeleteEmployeeDocumentHandler(
    DocumentsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        DeleteEmployeeDocumentRequest request,
        Guid deletedBy,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request. Payload-less success, so a
        // trivial `bool` marker is persisted/replayed purely to detect a repeated delivery.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, bool>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success();
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var row = await (
            from ed in db.EmployeeDocuments
            join d  in db.Documents     on ed.DocumentId    equals d.Id
            join dt in db.DocumentTypes on d.DocumentTypeId equals dt.Id
            where ed.Id        == request.EmployeeDocumentId
               && ed.CompanyId == request.CompanyId
               && ed.EmployeeId == request.EmployeeId
               && !ed.IsArchived
            select new { ed, d, DocumentTypeName = dt.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure(Error.NotFound("Employee document was not found."));

        var now = clock.UtcNowOffset();
        row.ed.Archive(deletedBy, request.Reason, now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status204NoContent, true, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success();
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new EmployeeDocumentArchivedAuditEvent(
            request.CompanyId,
            row.ed.Id,
            request.EmployeeId,
            row.d.Title,
            row.DocumentTypeName,
            row.ed.ArchiveReason,
            deletedBy,
            now), cancellationToken);

        return Result.Success();
    }
}
