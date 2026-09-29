using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal sealed class ProfilePhotoLocalStorageObjectResolver(DocumentsDbContext db) : ILocalStorageObjectResolver
{
    public string Bucket => LocalStorageBuckets.ProfilePhotos;

    public async Task<bool> IsServableAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (await db.EmployeeProfilePhotos.AsNoTracking()
                .AnyAsync(p => p.StorageKey == storageKey && p.ScanStatus == FileScanStatus.Clean, cancellationToken))
            return true;

        return await db.PendingProfilePhotos.AsNoTracking()
            .AnyAsync(p => p.StorageKey == storageKey && p.ScanStatus == FileScanStatus.Clean, cancellationToken);
    }
}
