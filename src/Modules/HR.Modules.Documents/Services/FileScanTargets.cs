using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

internal static class FileScanTargets
{
    public static Task<IScannableFile?> LoadAsync(
        DocumentsDbContext db, FileScanTargetType targetType, Guid entityId, CancellationToken cancellationToken) => targetType switch
    {
        FileScanTargetType.Document =>
            FindAsync(db.Documents, entityId, cancellationToken),
        FileScanTargetType.EmployeeProfilePhoto =>
            FindAsync(db.EmployeeProfilePhotos, entityId, cancellationToken),
        FileScanTargetType.PendingProfilePhoto =>
            FindAsync(db.PendingProfilePhotos, entityId, cancellationToken),
        FileScanTargetType.SharedCompanyDocument =>
            FindAsync(db.SharedCompanyDocuments, entityId, cancellationToken),
        FileScanTargetType.SharedCompanyDocumentVersion =>
            FindAsync(db.SharedCompanyDocumentVersions, entityId, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, null),
    };

    public static bool IsFinal(FileScanStatus status) =>
        status is FileScanStatus.Clean or FileScanStatus.Infected or FileScanStatus.Failed;

    private static async Task<IScannableFile?> FindAsync<TEntity>(
        DbSet<TEntity> set, Guid id, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await set.FindAsync([id], cancellationToken);
        return entity as IScannableFile;
    }
}
