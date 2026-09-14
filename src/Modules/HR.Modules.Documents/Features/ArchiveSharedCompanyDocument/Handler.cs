using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;

internal sealed class ArchiveSharedCompanyDocumentHandler(
    DocumentsDbContext db,
    ITaskCanceller taskCanceller,
    IAuditEventPublisher auditPublisher,
    IClock clock)
{
    public async Task<Result<ArchiveSharedCompanyDocumentResponse>> HandleAsync(
        ArchiveSharedCompanyDocumentRequest request,
        Guid archivedBy,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the archive.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ArchiveSharedCompanyDocumentResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ArchiveSharedCompanyDocumentResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var document = await db.SharedCompanyDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.CompanyId == request.CompanyId, cancellationToken);

        if (document is null)
            return Result.Failure<ArchiveSharedCompanyDocumentResponse>(
                Error.NotFound($"Shared document '{request.DocumentId}' was not found."));

        if (document.Status == SharedCompanyDocumentStatus.Archived)
            return Result.Failure<ArchiveSharedCompanyDocumentResponse>(
                Error.Conflict("This document is already archived."));

        var now = clock.UtcNowOffset();
        var reason = request.Reason.Trim();
        document.Archive(archivedBy, reason, now);

        // Cancelling happens after the archive save below (it's a separate call against the Tasks
        // module, not part of this DbContext's unit of work), so the cancelled-task count can't be
        // known before the save. Response therefore can't be built purely from in-memory values
        // ahead of the save for this handler — persisted first, then finalised.
        await db.SaveChangesAsync(cancellationToken);

        // Cancelling is safe/correct regardless of RequiresAcknowledgement's current value — a
        // document could have had acknowledgement required at some point with tasks still
        // outstanding, even if the setting was later toggled off. No-op if nothing is open.
        var cancelledCount = await taskCanceller.CancelAllBySourceEntityAsync(
            request.CompanyId,
            document.Id,
            TaskSource.Document,
            TaskActionType.Acknowledge,
            cancellationToken);

        var response = new ArchiveSharedCompanyDocumentResponse(
            document.Id,
            document.CompanyId,
            document.Status.ToString(),
            document.ArchivedBy!.Value,
            document.ArchivedAt!.Value,
            document.ArchiveReason!,
            cancelledCount);

        if (request.IdempotencyKey is { } key)
        {
            // The business save already happened above; this call only persists the idempotency
            // record itself (with the final response, including the task-cancellation count) so a
            // retry of the same key can be replayed. A concurrent same-key racer would already have
            // failed the earlier precheck or hit the unique-key insert below and rolled back
            // *its own* record — the archive itself was already committed by whichever request won
            // the underlying document-status check, so there is no risk of double-archiving.
            await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);
        }

        await auditPublisher.PublishAsync(new SharedCompanyDocumentArchivedAuditEvent(
            document.CompanyId,
            document.Id,
            document.Title,
            reason,
            cancelledCount,
            archivedBy,
            now), cancellationToken);

        return Result.Success(response);
    }
}
