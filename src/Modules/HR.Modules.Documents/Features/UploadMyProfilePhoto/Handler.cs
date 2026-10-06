using HR.Modules.Tasks.Contracts;
using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Features.UploadMyProfilePhoto;

internal sealed class UploadMyProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage,
    IImageUploadValidator imageValidator,
    ITaskCreator taskCreator,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IEmployeeNameReader employeeNameReader,
    IBackgroundJobClient backgroundJobClient,
    ILogger<UploadMyProfilePhotoHandler>? logger = null)
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

        var contentResult = imageValidator.ValidateImageContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadMyProfilePhotoResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        var storageKey = await storage.UploadAsync(
            fileStream,
            file.FileName,
            file.ContentType,
            ProfilePhotoStorageKeys.QuarantineFolder($"{request.CompanyId}/{employeeId}/pending"),
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
            await StorageCleanup.TryDeleteAsync(
                storage, logger, "UploadMyProfilePhoto", storageKey, request.CompanyId, pendingPhoto.Id);
            throw;
        }

        if (oldStorageKey is not null)
        {
            await StorageCleanup.TryDeleteAsync(
                storage, logger, "UploadMyProfilePhoto.ReplacedPendingPhoto", oldStorageKey, request.CompanyId, pendingPhoto.Id);
        }

        var names = await employeeNameReader.GetNamesAsync(request.CompanyId, [employeeId], cancellationToken);
        var employeeName = names.TryGetValue(employeeId, out var name) ? name : "an employee";

        await taskCreator.CreateAsync(
            request.CompanyId,
            createdBy:          employeeId,
            title:              $"Review profile photo — {employeeName}",
            description:        "An employee has submitted a new profile photo for review.",
            priority:           TaskPriority.Low,
            source:             TaskSource.Document,
            actionType:         TaskActionType.Review,
            dueDate:            null,
            assignedEmployeeId: null,
            assignedUserId:     null,
            sourceEntityId:     pendingPhoto.Id,
            cancellationToken,
            idempotencyKey:     $"ProfilePhotoReview:{pendingPhoto.Id}");

        await auditPublisher.PublishAsync(new ProfilePhotoSubmittedAuditEvent(
            pendingPhoto.CompanyId,
            pendingPhoto.Id,
            pendingPhoto.EmployeeId,
            pendingPhoto.FileName,
            pendingPhoto.FileSize,
            employeeId,
            now), cancellationToken);

        backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
            job.ExecuteAsync(FileScanTargetType.PendingProfilePhoto, pendingPhoto.Id, pendingPhoto.CompanyId, null));

        return Result.Success(new UploadMyProfilePhotoResponse(
            pendingPhoto.Id,
            pendingPhoto.CompanyId,
            pendingPhoto.EmployeeId,
            pendingPhoto.FileName,
            pendingPhoto.FileSize,
            pendingPhoto.ContentType,
            pendingPhoto.ScanStatus.ToString(),
            pendingPhoto.CreatedAt,
            pendingPhoto.UpdatedAt));
    }
}
