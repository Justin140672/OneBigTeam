using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.ApproveProfilePhoto;

internal sealed class ApproveProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage,
    ITaskCompleter taskCompleter,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<ApproveProfilePhotoResponse>> HandleAsync(
        ApproveProfilePhotoRequest request,
        Guid reviewerId,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-apply the approval.
                var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);
        

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ApproveProfilePhotoResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<ApproveProfilePhotoResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var pendingPhoto = await db.PendingProfilePhotos
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (pendingPhoto is null)
            return Result.Failure<ApproveProfilePhotoResponse>(
                Error.NotFound("No pending profile photo submission was found."));

        var now = clock.UtcNowOffset();

        var existingLivePhoto = await db.EmployeeProfilePhotos
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId,
                cancellationToken);

        string? oldLiveStorageKey = null;
        EmployeeProfilePhoto livePhoto;

        // Reuse the pending submission's already-uploaded blob rather than re-uploading it.
        if (existingLivePhoto is not null)
        {
            oldLiveStorageKey = existingLivePhoto.StorageKey;
            existingLivePhoto.Replace(
                pendingPhoto.FileName,
                pendingPhoto.FileSize,
                pendingPhoto.ContentType,
                pendingPhoto.StorageKey,
                pendingPhoto.UploadedBy,
                now);
            livePhoto = existingLivePhoto;
        }
        else
        {
            livePhoto = EmployeeProfilePhoto.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                pendingPhoto.FileName,
                pendingPhoto.FileSize,
                pendingPhoto.ContentType,
                pendingPhoto.StorageKey,
                pendingPhoto.UploadedBy,
                now);

            db.EmployeeProfilePhotos.Add(livePhoto);
        }

        // Create/Replace both set ScanStatus back to Pending (correct for a genuinely new upload)
        // — but this reuses the pending submission's already-scanned blob rather than uploading a
        // new one (see the comment above), and nothing ever enqueues a scan job against this
        // EmployeeProfilePhoto row afterward (unlike the direct-HR-upload path, which does).
        // Left as Pending, ScanStatusAccessGuard.CheckDownloadable rejects it forever — the photo
        // would never become downloadable no matter how long a caller waits. The pending photo can
        // only have reached this point via the same upload pipeline that already scanned it clean.
        livePhoto.MarkScanClean(now);

        var pendingPhotoId = pendingPhoto.Id;
        db.PendingProfilePhotos.Remove(pendingPhoto);

        // Fetched ahead of the save (doesn't depend on it) so the response can be built from
        // in-memory values before persisting, doubling as both the response and the payload
        // persisted for an idempotency replay.
        var downloadUrl = await storage.GetDownloadUrlAsync(livePhoto.StorageKey, cancellationToken);

        var response = new ApproveProfilePhotoResponse(
            livePhoto.Id,
            livePhoto.CompanyId,
            livePhoto.EmployeeId,
            livePhoto.FileName,
            livePhoto.FileSize,
            livePhoto.ContentType,
            downloadUrl.ToString(),
            livePhoto.CreatedAt,
            livePhoto.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
            scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            // Lost a race against a concurrent duplicate under the same key — this attempt's
            // changes were rolled back along with it, so skip the blob deletion/task-complete/
            // notification/audit publishing below and hand back the winner's result untouched.
            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (oldLiveStorageKey is not null)
        {
            // Only remove the old blob once the new one is safely persisted.
            try { await storage.DeleteAsync(oldLiveStorageKey, cancellationToken); } catch { }
        }

        await taskCompleter.CompleteBySourceEntityAsync(
            request.CompanyId,
            pendingPhotoId,
            TaskSource.Document,
            TaskActionType.Review,
            completedBy: reviewerId,
            cancellationToken);

        await notificationWriter.WriteAsync(
            Guid.NewGuid(),
            request.CompanyId,
            request.EmployeeId,
            "Your profile photo has been approved",
            "Your profile photo submission has been reviewed and approved.",
            pendingPhotoId,
            NotificationType.ProfilePhotoApproved,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        await auditPublisher.PublishAsync(new ProfilePhotoApprovedAuditEvent(
            livePhoto.CompanyId,
            livePhoto.Id,
            livePhoto.EmployeeId,
            livePhoto.FileName,
            livePhoto.FileSize,
            reviewerId,
            now), cancellationToken);

        return Result.Success(response);
    }
}
