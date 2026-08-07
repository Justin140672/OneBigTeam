using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;

internal sealed class UploadEmployeeProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage,
    IImageUploadValidator imageValidator,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<UploadEmployeeProfilePhotoResponse>> HandleAsync(
        UploadEmployeeProfilePhotoRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        var validationResult = imageValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadEmployeeProfilePhotoResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        // Virus scanning happens asynchronously via ScanUploadedFileJob (enqueued below) rather
        // than inline — the row is stored with ScanStatus = Pending.
        // Verify file content matches the declared content type and that its pixel dimensions
        // fall within the configured bounds (prevents extension/MIME spoofing).
        var contentResult = imageValidator.ValidateImageContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadEmployeeProfilePhotoResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            $"{request.CompanyId}/{request.EmployeeId}",
            cancellationToken);

        var now = clock.UtcNowOffset();

        var existingPhoto = await db.EmployeeProfilePhotos
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId,
                cancellationToken);

        string? oldStorageKey = null;
        EmployeeProfilePhoto photo;

        if (existingPhoto is not null)
        {
            oldStorageKey = existingPhoto.StorageKey;
            existingPhoto.Replace(file.FileName, file.Length, file.ContentType, storageKey, uploadedBy, now);
            photo = existingPhoto;
        }
        else
        {
            photo = EmployeeProfilePhoto.Create(
                Guid.NewGuid(),
                request.CompanyId,
                request.EmployeeId,
                file.FileName,
                file.Length,
                file.ContentType,
                storageKey,
                uploadedBy,
                now);

            db.EmployeeProfilePhotos.Add(photo);
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

        await auditPublisher.PublishAsync(new ProfilePhotoUploadedAuditEvent(
            photo.CompanyId,
            photo.Id,
            photo.EmployeeId,
            photo.FileName,
            photo.FileSize,
            uploadedBy,
            IsManagerUpload: uploadedBy != request.EmployeeId,
            IsReplace: oldStorageKey is not null,
            now), cancellationToken);

        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.EmployeeProfilePhoto, photo.Id, photo.CompanyId, null));

        var downloadUrl = await storage.GetDownloadUrlAsync(photo.StorageKey, cancellationToken);

        return Result.Success(new UploadEmployeeProfilePhotoResponse(
            photo.Id,
            photo.CompanyId,
            photo.EmployeeId,
            photo.FileName,
            photo.FileSize,
            photo.ContentType,
            downloadUrl.ToString(),
            photo.CreatedAt,
            photo.UpdatedAt));
    }
}
