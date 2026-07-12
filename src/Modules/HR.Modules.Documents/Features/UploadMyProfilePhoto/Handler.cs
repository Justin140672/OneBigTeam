using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadMyProfilePhoto;

internal sealed class UploadMyProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage,
    IImageUploadValidator imageValidator,
    IVirusScanService virusScanner,
    ITaskCreator taskCreator,
    IClock clock,
    IAuditEventPublisher auditPublisher)
{
    public async Task<Result<UploadMyProfilePhotoResponse>> HandleAsync(
        UploadMyProfilePhotoRequest request,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        var validationResult = imageValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadMyProfilePhotoResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        var scanResult = await virusScanner.ScanAsync(fileStream, file.FileName, cancellationToken);
        if (!scanResult.IsClean)
            return Result.Failure<UploadMyProfilePhotoResponse>(
                Error.Validation($"File was rejected: {scanResult.ThreatName}."));

        fileStream.Seek(0, SeekOrigin.Begin);

        // Verify file content matches the declared content type and that its pixel dimensions
        // fall within the configured bounds (prevents extension/MIME spoofing).
        var contentResult = imageValidator.ValidateImageContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadMyProfilePhotoResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            $"{request.CompanyId}/{employeeId}/pending",
            cancellationToken);

        var now = clock.UtcNowOffset();

        var existingPending = await db.PendingProfilePhotos
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == employeeId,
                cancellationToken);

        string? oldStorageKey = null;
        PendingProfilePhoto pendingPhoto;

        if (existingPending is not null)
        {
            oldStorageKey = existingPending.StorageKey;
            existingPending.Replace(file.FileName, file.Length, file.ContentType, storageKey, employeeId, now);
            pendingPhoto = existingPending;
        }
        else
        {
            pendingPhoto = PendingProfilePhoto.Create(
                Guid.NewGuid(),
                request.CompanyId,
                employeeId,
                file.FileName,
                file.Length,
                file.ContentType,
                storageKey,
                employeeId,
                now);

            db.PendingProfilePhotos.Add(pendingPhoto);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Best-effort: remove the already-uploaded file so it doesn't become an orphan.
            try { await storage.DeleteAsync(storageKey, cancellationToken); } catch { }
            throw;
        }

        if (oldStorageKey is not null)
        {
            // Only remove the old blob once the new one is safely persisted.
            try { await storage.DeleteAsync(oldStorageKey, cancellationToken); } catch { }
        }

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          employeeId,
            title:              $"Review profile photo — {employeeId}",
            description:        "An employee has submitted a new profile photo for review.",
            priority:           TaskPriority.Low,
            source:             TaskSource.Document,
            actionType:         TaskActionType.Review,
            dueDate:            null,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     pendingPhoto.Id,
            cancellationToken);

        await auditPublisher.PublishAsync(new ProfilePhotoSubmittedAuditEvent(
            pendingPhoto.CompanyId,
            pendingPhoto.Id,
            pendingPhoto.EmployeeId,
            pendingPhoto.FileName,
            pendingPhoto.FileSize,
            employeeId,
            now), cancellationToken);

        var downloadUrl = await storage.GetDownloadUrlAsync(pendingPhoto.StorageKey, cancellationToken);

        return Result.Success(new UploadMyProfilePhotoResponse(
            pendingPhoto.Id,
            pendingPhoto.CompanyId,
            pendingPhoto.EmployeeId,
            pendingPhoto.FileName,
            pendingPhoto.FileSize,
            pendingPhoto.ContentType,
            downloadUrl.ToString(),
            pendingPhoto.CreatedAt,
            pendingPhoto.UpdatedAt));
    }
}
