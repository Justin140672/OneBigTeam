using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
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

        await db.SaveChangesAsync(cancellationToken);

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

        var downloadUrl = await storage.GetDownloadUrlAsync(livePhoto.StorageKey, cancellationToken);

        return Result.Success(new ApproveProfilePhotoResponse(
            livePhoto.Id,
            livePhoto.CompanyId,
            livePhoto.EmployeeId,
            livePhoto.FileName,
            livePhoto.FileSize,
            livePhoto.ContentType,
            downloadUrl.ToString(),
            livePhoto.CreatedAt,
            livePhoto.UpdatedAt));
    }
}
