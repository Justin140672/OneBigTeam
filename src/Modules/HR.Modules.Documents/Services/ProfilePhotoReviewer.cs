using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ApproveProfilePhoto;
using HR.Modules.Documents.Features.RejectProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Owns the actual approve/reject persistence, notification and audit logic shared by
/// <see cref="ApproveProfilePhotoHandler"/>/<see cref="RejectProfilePhotoHandler"/> (the dedicated
/// review endpoints) and <see cref="Features.CompleteProfilePhotoReviewFromTask.CompleteProfilePhotoReviewFromTaskAction"/>
/// (the generic Tasks "Complete task" path).
///
/// Deliberately does NOT depend on <c>ITaskCompleter</c> — that dependency belongs only to the
/// dedicated endpoint handlers, which must themselves resolve and close the linked review task.
/// The task-dispatch path must NEVER take that dependency here: ITaskCompleter's own
/// implementation resolves through TaskCompletionDispatcher, which enumerates every registered
/// ITaskCompletionAction — including CompleteProfilePhotoReviewFromTaskAction — so if this service
/// (used by that very action) also depended on ITaskCompleter, the container would have a circular
/// dependency (ITaskCompleter -> TaskCompletionDispatcher -> ITaskCompletionAction ->
/// CompleteProfilePhotoReviewFromTaskAction -> this service -> ITaskCompleter). The task-dispatch
/// path doesn't need it anyway: CompleteTaskHandler completes the TaskItem itself once dispatch
/// succeeds.
/// </summary>
internal sealed class ProfilePhotoReviewer(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    INotificationWriter notificationWriter)
{
    public async Task<(Result<ApproveProfilePhotoResponse> Result, Guid? PendingProfilePhotoId)> ApproveAsync(
        Guid companyId,
        Guid employeeId,
        Guid reviewerId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(nameof(ApproveProfilePhotoHandler), companyId, Guid.Empty);

        var fingerprintSource = new ApproveProfilePhotoRequest(companyId, employeeId);
        var fingerprint = idempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(fingerprintSource)
            : null;

        if (idempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, ApproveProfilePhotoResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return (Result.Success(replay.Response!), null);
                case IdempotencyOutcomeKind.KeyReused:
                    return (Result.Failure<ApproveProfilePhotoResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request.")), null);
            }
        }

        var pendingPhoto = await db.PendingProfilePhotos
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId, cancellationToken);

        if (pendingPhoto is null)
            return (Result.Failure<ApproveProfilePhotoResponse>(
                Error.NotFound("No pending profile photo submission was found.")), null);

        var scanError = ScanStatusAccessGuard.CheckDownloadable(pendingPhoto.ScanStatus);
        if (scanError is not null)
            return (Result.Failure<ApproveProfilePhotoResponse>(scanError), null);

        var now = clock.UtcNowOffset();

        var existingLivePhoto = await db.EmployeeProfilePhotos
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId, cancellationToken);

        string? oldLiveStorageKey = null;
        EmployeeProfilePhoto livePhoto;

        if (existingLivePhoto is not null)
        {
            oldLiveStorageKey = existingLivePhoto.StorageKey;
            existingLivePhoto.Replace(
                pendingPhoto.FileName, pendingPhoto.FileSize, pendingPhoto.ContentType,
                pendingPhoto.StorageKey, pendingPhoto.UploadedBy, now);
            livePhoto = existingLivePhoto;
        }
        else
        {
            livePhoto = EmployeeProfilePhoto.Create(
                Guid.NewGuid(), companyId, employeeId,
                pendingPhoto.FileName, pendingPhoto.FileSize, pendingPhoto.ContentType,
                pendingPhoto.StorageKey, pendingPhoto.UploadedBy, now);

            db.EmployeeProfilePhotos.Add(livePhoto);
        }

        livePhoto.MarkScanClean(now);

        var pendingPhotoId = pendingPhoto.Id;
        db.PendingProfilePhotos.Remove(pendingPhoto);

        var downloadUrl = await storage.GetDownloadUrlAsync(livePhoto.StorageKey, cancellationToken);

        var response = new ApproveProfilePhotoResponse(
            livePhoto.Id, livePhoto.CompanyId, livePhoto.EmployeeId, livePhoto.FileName, livePhoto.FileSize,
            livePhoto.ContentType, downloadUrl.ToString(), livePhoto.CreatedAt, livePhoto.UpdatedAt);

        if (idempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return (Result.Success(outcome.Response!), null);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (oldLiveStorageKey is not null)
        {
            try { await storage.DeleteAsync(oldLiveStorageKey, cancellationToken); } catch { }
        }

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Your profile photo has been approved",
            "Your profile photo submission has been reviewed and approved.",
            pendingPhotoId,
            NotificationType.ProfilePhotoApproved,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        await auditPublisher.PublishAsync(new ProfilePhotoApprovedAuditEvent(
            livePhoto.CompanyId, livePhoto.Id, livePhoto.EmployeeId, livePhoto.FileName, livePhoto.FileSize,
            reviewerId, now), cancellationToken);

        return (Result.Success(response), pendingPhotoId);
    }

    public async Task<(Result<RejectProfilePhotoResponse> Result, Guid? PendingProfilePhotoId)> RejectAsync(
        Guid companyId,
        Guid employeeId,
        Guid reviewerId,
        string? rejectionReason,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(nameof(RejectProfilePhotoHandler), companyId, Guid.Empty);

        var fingerprintSource = new RejectProfilePhotoRequest(companyId, employeeId, rejectionReason);
        var fingerprint = idempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(fingerprintSource)
            : null;

        if (idempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, RejectProfilePhotoResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return (Result.Success(replay.Response!), null);
                case IdempotencyOutcomeKind.KeyReused:
                    return (Result.Failure<RejectProfilePhotoResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request.")), null);
            }
        }

        var pendingPhoto = await db.PendingProfilePhotos
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId, cancellationToken);

        if (pendingPhoto is null)
            return (Result.Failure<RejectProfilePhotoResponse>(
                Error.NotFound("No pending profile photo submission was found.")), null);

        var now = clock.UtcNowOffset();
        var pendingPhotoId = pendingPhoto.Id;
        var storageKey = pendingPhoto.StorageKey;

        db.PendingProfilePhotos.Remove(pendingPhoto);

        var response = new RejectProfilePhotoResponse(pendingPhotoId, employeeId, rejectionReason, reviewerId, now);

        if (idempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync(db.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return (Result.Success(outcome.Response!), null);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }

        var body = rejectionReason is not null
            ? $"Your profile photo submission has been rejected. Reason: {rejectionReason}"
            : "Your profile photo submission has been rejected.";

        await notificationWriter.WriteAsync(
            Guid.NewGuid(), companyId, employeeId,
            "Your profile photo has been rejected",
            body,
            pendingPhotoId,
            NotificationType.ProfilePhotoRejected,
            NotificationPriority.Normal,
            now,
            cancellationToken);

        await auditPublisher.PublishAsync(new ProfilePhotoRejectedAuditEvent(
            companyId, pendingPhotoId, employeeId, reviewerId, rejectionReason, now), cancellationToken);

        return (Result.Success(response), pendingPhotoId);
    }
}
