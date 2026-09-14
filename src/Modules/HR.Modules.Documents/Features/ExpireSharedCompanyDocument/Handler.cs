using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ExpireSharedCompanyDocument;

internal sealed class ExpireSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    ITaskCanceller taskCanceller,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<ExpireSharedCompanyDocumentResponse>> HandleAsync(
        ExpireSharedCompanyDocumentRequest request,
        Guid expiredBy,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the expiry.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ExpireSharedCompanyDocumentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ExpireSharedCompanyDocumentResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<ExpireSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (document.Status == SharedCompanyDocumentStatus.Expired)
            return Result.Failure<ExpireSharedCompanyDocumentResponse>(
                Error.Conflict("This document is already expired."));

        if (document.Status == SharedCompanyDocumentStatus.Archived)
            return Result.Failure<ExpireSharedCompanyDocumentResponse>(
                Error.Conflict("An archived document cannot be marked expired."));

        var now = clock.UtcNowOffset();
        document.MarkExpired(expiredBy, now);

        // Cancelling happens after the save below (it's a separate call against the Tasks module,
        // not part of this DbContext's unit of work), so the cancelled-task count can't be known
        // before the save — the response therefore can't be built purely from in-memory values
        // ahead of the save for this handler, unlike CreateAsset/AdjustLeaveBalance.
        await db.SaveChangesAsync(cancellationToken);

        // An open "please review this" task makes no sense once the document is expired and
        // won't be renewed — same rationale/pattern as ArchiveSharedCompanyDocumentHandler
        // cancelling open Acknowledge tasks when archiving. No-op if nothing is open.
        var cancelledCount = await taskCanceller.CancelAllBySourceEntityAsync(
            request.CompanyId,
            document.Id,
            TaskSource.Document,
            TaskActionType.Review,
            cancellationToken);

        var response = new ExpireSharedCompanyDocumentResponse(
            document.Id,
            document.CompanyId,
            document.Status.ToString(),
            document.ExpiredBy!.Value,
            document.ExpiredAt!.Value,
            cancelledCount);

        if (request.IdempotencyKey is { } key)
        {
            // The business save already happened above; this only persists the idempotency record
            // itself (with the final response, including the task-cancellation count) so a retry of
            // the same key can be replayed — the status-check guards above already prevent
            // double-expiry regardless of the key.
            await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);
        }

        await auditPublisher.PublishAsync(new SharedCompanyDocumentExpiredAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            cancelledCount,
            expiredBy,
            now), cancellationToken);

        return Result.Success(response);
    }
}
