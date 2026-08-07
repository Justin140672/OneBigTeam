using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetPendingProfilePhotoById;

internal sealed class GetPendingProfilePhotoByIdHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage)
{
    public async Task<Result<GetPendingProfilePhotoByIdResponse>> HandleAsync(
        GetPendingProfilePhotoByIdRequest request,
        CancellationToken cancellationToken)
    {
        var pendingPhoto = await db.PendingProfilePhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Id == request.PendingPhotoId && p.CompanyId == request.CompanyId,
                cancellationToken);

        if (pendingPhoto is null)
            return Result.Failure<GetPendingProfilePhotoByIdResponse>(
                Error.NotFound("No pending profile photo submission was found."));

        var scanError = ScanStatusAccessGuard.CheckDownloadable(pendingPhoto.ScanStatus);
        if (scanError is not null)
            return Result.Failure<GetPendingProfilePhotoByIdResponse>(scanError);

        var downloadUrl = await storage.GetDownloadUrlAsync(pendingPhoto.StorageKey, cancellationToken);

        return Result.Success(new GetPendingProfilePhotoByIdResponse(
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
