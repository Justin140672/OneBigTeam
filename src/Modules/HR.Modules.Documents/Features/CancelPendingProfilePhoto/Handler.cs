using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.CancelPendingProfilePhoto;

internal sealed class CancelPendingProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage,
    ITaskCanceller taskCanceller,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result> HandleAsync(
        CancelPendingProfilePhotoRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work. This handler's success response is payload-less (Result), so a trivial `bool`
        // marker is persisted/replayed purely to detect a repeated delivery — its value is unused.
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

        var pendingPhoto = await db.PendingProfilePhotos
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == employeeId,
                cancellationToken);

        if (pendingPhoto is null)
            return Result.Failure(Error.NotFound("No pending profile photo submission was found."));

        try { await storage.DeleteAsync(pendingPhoto.StorageKey, cancellationToken); } catch { }

        db.PendingProfilePhotos.Remove(pendingPhoto);

        var now = clock.UtcNowOffset();

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status204NoContent, true, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key — this attempt's
            // removal was rolled back along with it, so skip the task-cancel/audit publishing
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
            pendingPhoto.Id,
            TaskSource.Document,
            TaskActionType.Review,
            cancellationToken);

        await auditPublisher.PublishAsync(new ProfilePhotoCancelledAuditEvent(
            request.CompanyId,
            pendingPhoto.Id,
            employeeId,
            employeeId,
            clock.UtcNowOffset()), cancellationToken);

        return Result.Success();
    }
}
