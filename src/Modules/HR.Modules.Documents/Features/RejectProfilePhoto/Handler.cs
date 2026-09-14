using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.RejectProfilePhoto;

internal sealed class RejectProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage,
    ITaskCompleter taskCompleter,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<RejectProfilePhotoResponse>> HandleAsync(
        RejectProfilePhotoRequest request,
        Guid reviewerId,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the rejection.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, RejectProfilePhotoResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<RejectProfilePhotoResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var pendingPhoto = await db.PendingProfilePhotos
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (pendingPhoto is null)
            return Result.Failure<RejectProfilePhotoResponse>(
                Error.NotFound("No pending profile photo submission was found."));

        var now = clock.UtcNowOffset();
        var pendingPhotoId = pendingPhoto.Id;
        var storageKey = pendingPhoto.StorageKey;

        db.PendingProfilePhotos.Remove(pendingPhoto);

        var response = new RejectProfilePhotoResponse(
            pendingPhotoId,
            request.EmployeeId,
            request.RejectionReason,
            reviewerId,
            now);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key — this attempt's
            // removal was rolled back along with it, so skip the blob-delete/task-complete/
            // notification/audit publishing below and hand back the winner's result untouched.
            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }

        await taskCompleter.CompleteBySourceEntityAsync(
            request.CompanyId,
            pendingPhotoId,
            TaskSource.Document,
            TaskActionType.Review,
            completedBy: reviewerId,
            cancellationToken);

        var body = request.RejectionReason is not null
            ? $"Your profile photo submission has been rejected. Reason: {request.RejectionReason}"
            : "Your profile photo submission has been rejected.";

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            "Your profile photo has been rejected",
            body,
            pendingPhotoId,
            NotificationType.ProfilePhotoRejected,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        await auditPublisher.PublishAsync(new ProfilePhotoRejectedAuditEvent(
            request.CompanyId,
            pendingPhotoId,
            request.EmployeeId,
            reviewerId,
            request.RejectionReason,
            now), cancellationToken);

        return Result.Success(response);
    }
}
