using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Dev-only local delivery re-check for the <see cref="LocalStorageBuckets.ProfilePhotos"/> bucket (the
/// Documents module owns both profile-photo tables even though the storage service lives in
/// HR.Infrastructure). A key is servable only while it still belongs to a current or pending profile
/// photo whose malware scan is Clean, the same gate every profile-photo read handler applies via
/// <see cref="ScanStatusAccessGuard"/>.
/// </summary>
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
