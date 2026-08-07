using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetMyProfilePhoto;

internal sealed class GetMyProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage)
{
    public async Task<GetMyProfilePhotoResponse> HandleAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var currentPhoto = await db.EmployeeProfilePhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId, cancellationToken);

        var pendingPhoto = await db.PendingProfilePhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId, cancellationToken);

        // A photo is only ever exposed for download once its virus scan has come back Clean.
        // Callers still see that a submission exists (via the pending record's other data on
        // this page, if any is later added) but no download URL is handed out until it's safe.
        CurrentPhotoDto? currentPhotoDto = null;
        if (currentPhoto is not null && ScanStatusAccessGuard.CheckDownloadable(currentPhoto.ScanStatus) is null)
        {
            var downloadUrl = await storage.GetDownloadUrlAsync(currentPhoto.StorageKey, cancellationToken);
            currentPhotoDto = new CurrentPhotoDto(
                currentPhoto.Id,
                currentPhoto.FileName,
                currentPhoto.FileSize,
                currentPhoto.ContentType,
                downloadUrl.ToString(),
                currentPhoto.UpdatedAt);
        }

        PendingPhotoDto? pendingPhotoDto = null;
        if (pendingPhoto is not null && ScanStatusAccessGuard.CheckDownloadable(pendingPhoto.ScanStatus) is null)
        {
            var downloadUrl = await storage.GetDownloadUrlAsync(pendingPhoto.StorageKey, cancellationToken);
            pendingPhotoDto = new PendingPhotoDto(
                pendingPhoto.Id,
                pendingPhoto.FileName,
                pendingPhoto.FileSize,
                pendingPhoto.ContentType,
                downloadUrl.ToString(),
                pendingPhoto.CreatedAt);
        }

        return new GetMyProfilePhotoResponse(currentPhotoDto, pendingPhotoDto);
    }
}
