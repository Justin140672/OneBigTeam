using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.SharedKernel;
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
        var pendingPhoto = await db.PendingProfilePhotos
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == employeeId,
                cancellationToken);

        if (pendingPhoto is null)
            return Result.Failure(Error.NotFound("No pending profile photo submission was found."));

        try { await storage.DeleteAsync(pendingPhoto.StorageKey, cancellationToken); } catch { }

        db.PendingProfilePhotos.Remove(pendingPhoto);
        await db.SaveChangesAsync(cancellationToken);

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
