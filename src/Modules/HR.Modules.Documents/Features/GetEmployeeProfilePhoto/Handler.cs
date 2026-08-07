using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Features.GetEmployeeProfilePhoto;

internal sealed class GetEmployeeProfilePhotoHandler(
    DocumentsDbContext db,
    IProfilePhotoStorageService storage)
{
    public async Task<Result<GetEmployeeProfilePhotoResponse>> HandleAsync(
        GetEmployeeProfilePhotoRequest request,
        CancellationToken cancellationToken)
    {
        var profilePhoto = await db.EmployeeProfilePhotos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.CompanyId == request.CompanyId && p.EmployeeId == request.EmployeeId,
                cancellationToken);

        if (profilePhoto is null)
            return Result.Failure<GetEmployeeProfilePhotoResponse>(
                Error.NotFound("No profile photo was found for this employee."));

        var scanError = ScanStatusAccessGuard.CheckDownloadable(profilePhoto.ScanStatus);
        if (scanError is not null)
            return Result.Failure<GetEmployeeProfilePhotoResponse>(scanError);

        var downloadUrl = await storage.GetDownloadUrlAsync(profilePhoto.StorageKey, cancellationToken);

        return Result.Success(new GetEmployeeProfilePhotoResponse(
            profilePhoto.Id,
            profilePhoto.CompanyId,
            profilePhoto.EmployeeId,
            profilePhoto.FileName,
            profilePhoto.FileSize,
            profilePhoto.ContentType,
            downloadUrl.ToString(),
            profilePhoto.CreatedAt,
            profilePhoto.UpdatedAt));
    }
}
