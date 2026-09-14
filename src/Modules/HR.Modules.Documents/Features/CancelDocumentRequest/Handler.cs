using HR.Modules.Tasks.Contracts;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CancelDocumentRequest;

internal sealed class CancelDocumentRequestHandler(
    DocumentsDbContext db,
    ITaskCanceller taskCanceller,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        CancelDocumentRequestRequest request,
        Guid cancelledBy,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work. This handler's success response is payload-less (Result), so a trivial `bool`
        // marker is persisted/replayed purely to detect a repeated delivery — its value is unused.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(new CancelDocumentRequestRequest
            {
                CompanyId = request.CompanyId,
                EmployeeId = request.EmployeeId,
                DocumentRequestId = request.DocumentRequestId,
            })
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

        var documentRequest = await db.DocumentRequests
            .FirstOrDefaultAsync(
                r => r.Id == request.DocumentRequestId
                  && r.CompanyId == request.CompanyId
                  && r.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (documentRequest is null)
            return Result.Failure(Error.NotFound($"Document request '{request.DocumentRequestId}' was not found."));

        if (documentRequest.Status != DocumentRequestStatus.Requested)
            return Result.Failure(Error.Conflict(
                $"Document request cannot be cancelled (status: {documentRequest.Status})."));

        var documentType = await db.DocumentTypes
            .FirstOrDefaultAsync(dt => dt.Id == documentRequest.DocumentTypeId, cancellationToken);

        var now = clock.UtcNowOffset();
        var documentTypeName = documentType?.Name ?? documentRequest.DocumentTypeId.ToString();

        documentRequest.Cancel(now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status204NoContent, true, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key — this attempt's
            // cancellation was rolled back along with it, so skip the task-cancel/audit publishing
            // below; the winner's request already did it.
            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success();
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await taskCanceller.CancelBySourceEntityAsync(
            request.CompanyId,
            documentRequest.Id,
            TaskSource.Document,
            TaskActionType.Upload,
            cancellationToken);

        await auditPublisher.PublishAsync(new DocumentRequestCancelledAuditEvent(
            request.CompanyId,
            documentRequest.Id,
            request.EmployeeId,
            documentTypeName,
            cancelledBy,
            now), cancellationToken);

        return Result.Success();
    }
}
