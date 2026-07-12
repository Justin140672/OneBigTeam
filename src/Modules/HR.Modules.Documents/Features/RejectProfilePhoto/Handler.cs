using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
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
        await db.SaveChangesAsync(cancellationToken);

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

        return Result.Success(new RejectProfilePhotoResponse(
            pendingPhotoId,
            request.EmployeeId,
            request.RejectionReason,
            reviewerId,
            now));
    }
}
