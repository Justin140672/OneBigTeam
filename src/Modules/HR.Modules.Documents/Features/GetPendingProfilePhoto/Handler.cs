using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetPendingProfilePhoto;

internal sealed class GetPendingProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage)
{
    public async Task<Result<GetPendingProfilePhotoResponse>> HandleAsync(
        GetPendingProfilePhotoRequest request,
        CancellationToken cancellationToken)
    {
        var pendingPhoto = await db.PendingProfilePhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (pendingPhoto is null)
            return Result.Failure<GetPendingProfilePhotoResponse>(
                Error.NotFound("No pending profile photo submission was found."));

        var scanError = ScanStatusAccessGuard.CheckDownloadable(pendingPhoto.ScanStatus);
        if (scanError is not null)
            return Result.Failure<GetPendingProfilePhotoResponse>(scanError);

        var downloadUrl = await storage.GetDownloadUrlAsync(pendingPhoto.StorageKey, cancellationToken);

        return Result.Success(new GetPendingProfilePhotoResponse(
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
