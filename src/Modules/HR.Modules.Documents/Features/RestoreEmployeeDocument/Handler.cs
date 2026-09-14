using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.RestoreEmployeeDocument;

internal sealed class RestoreEmployeeDocumentHandler(
    DocumentsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<RestoreEmployeeDocumentResponse>> HandleAsync(
        RestoreEmployeeDocumentRequest request,
        Guid restoredBy,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the restore.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, RestoreEmployeeDocumentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RestoreEmployeeDocumentResponse>(
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
            select new { ed, d, DocumentTypeName = dt.Name })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
            return Result.Failure<RestoreEmployeeDocumentResponse>(
                Error.NotFound("Employee document was not found."));

        if (!row.ed.IsArchived)
            return Result.Failure<RestoreEmployeeDocumentResponse>(
                Error.Conflict("This document is not archived."));

        var now = clock.UtcNowOffset();
        row.ed.Restore(restoredBy, now);

        var response = new RestoreEmployeeDocumentResponse(row.ed.Id, row.ed.CompanyId, restoredBy, now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await auditPublisher.PublishAsync(new EmployeeDocumentRestoredAuditEvent(
            request.CompanyId,
            row.ed.Id,
            request.EmployeeId,
            row.d.Title,
            row.DocumentTypeName,
            restoredBy,
            now), cancellationToken);

        return Result.Success(response);
    }
}
