using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Dev-only local delivery re-check for the <see cref="LocalStorageBuckets.Documents"/> bucket. A key is
/// servable only while it still belongs to a Clean record that one of the module's authorised download
/// handlers would serve:
/// <list type="bullet">
/// <item>a Document linked to a non-archived EmployeeDocument (DownloadEmployeeDocument);</item>
/// <item>a SharedCompanyDocument's current file (DownloadSharedCompanyDocument);</item>
/// <item>a SharedCompanyDocumentVersion's file (DownloadSharedCompanyDocumentVersion).</item>
/// </list>
/// Caller identity/audience checks were already made by those handlers before the URL was signed.
/// </summary>
internal sealed class DocumentsLocalStorageObjectResolver(DocumentsDbContext db) : ILocalStorageObjectResolver
{
    public string Bucket => LocalStorageBuckets.Documents;

    public async Task<bool> IsServableAsync(string storageKey, CancellationToken cancellationToken)
    {
        var employeeDocument = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d in db.Documents.AsNoTracking() on ed.DocumentId equals d.Id
            where d.StorageKey == storageKey
               && !ed.IsArchived
               && d.ScanStatus == FileScanStatus.Clean
            select ed.Id
        ).AnyAsync(cancellationToken);

        if (employeeDocument)
            return true;

        if (await db.SharedCompanyDocuments.AsNoTracking()
                .AnyAsync(d => d.CurrentFileReference == storageKey && d.ScanStatus == FileScanStatus.Clean, cancellationToken))
            return true;

        return await db.SharedCompanyDocumentVersions.AsNoTracking()
            .AnyAsync(v => v.FileReference == storageKey && v.ScanStatus == FileScanStatus.Clean, cancellationToken);
    }
}
